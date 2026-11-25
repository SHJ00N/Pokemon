# 상태이상 마무리 테스트. 사용: python -I switch_test.py <Server.exe> <Pokemon.db> <결과 파일> <서버 코드 폴더>
import glob, io, json, os, re, socket, sqlite3, subprocess, sys, time

exe, db, out_path, code_dir = sys.argv[1:5]
out = io.open(out_path, "w", encoding="utf-8")
port = 5081
server = subprocess.Popen([exe, str(port)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
import atexit; atexit.register(server.kill)
for _ in range(75):  # 서버가 포트를 열 때까지 0.2초마다 확인(최대 15초)
    try:
        socket.create_connection(("127.0.0.1", port), timeout=1).close()
        break
    except OSError:
        time.sleep(0.2)
else:
    raise SystemExit(f"서버가 15초 안에 포트 {port}를 열지 않았다")
con = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
failures = []
PROMPTS = ("CHOOSE_ACTION", "CHOOSE_SWITCH", "WAIT_SWITCH", "LOCKED", "BATTLE_END")
p = lambda *a: print(*a, file=out, flush=True)

def check(cond, msg):
    p(("PASS " if cond else "FAIL ") + msg)
    if not cond:
        failures.append(msg)

def battle_code_files(code_dir):
    # 전투 규칙 코드: 옮기기 전 RoomManager*.cs, 옮긴 뒤 Battle/Battle.cs·Battle.*.cs와 Lobby·Single의 RoomManager*.cs
    pats = ["**/RoomManager*.cs", "**/Battle.cs", "**/Battle.*.cs"]
    return sorted({f for p in pats for f in glob.glob(os.path.join(code_dir, p), recursive=True)})

# 서버 코드에서 구현된 기술 번호를 모아 "아무 효과 없는 변화기"를 고른다.
code = "".join(open(f, encoding="utf-8").read() for f in battle_code_files(code_dir))
code = re.sub(r"//[^\n]*", "", code)   # 주석 제거
implemented = {int(x) for x in re.findall(r"\[(\d+)\] = \(", code)}
implemented |= {int(x) for x in re.findall(r"\[(\d+)\] = [A-Z]\w*", code)}   # [240] = Rain 등
implemented |= {int(x) for x in re.findall(r"MoveId\s*=\s*(\d+)", code)}
for body in re.findall(r"HashSet<int> \w+ =\s*\[([\d,\s]+)\]", code):
    implemented |= {int(x) for x in re.findall(r"\d+", body)}
implemented |= {m for (m,) in con.execute("SELECT DISTINCT move_id FROM MoveStatusEffect")}
implemented |= {m for (m,) in con.execute("SELECT DISTINCT move_id FROM MoveStatChange")}   # DB 능력치 변화 기술
NO_EFFECT = {m for (m,) in con.execute("SELECT move_id FROM PokemonMove WHERE category='변화'")} - implemented
NO_EFFECT |= {373, 511, 495, 478, 271, 415, 285, 272, 380, 388, 493, 494, 100, 516, 363, 597, 602}   # 항상 실패하는 우선도 0 기술
TYPES = {name: tid for tid, name in con.execute("SELECT type_id, name FROM Type")}
SPECIES = {pid: sid for pid, sid in con.execute("SELECT pokemon_id, species_id FROM Pokemon")}
name = lambda m: con.execute("SELECT name FROM PokemonMove WHERE move_id=?", (m,)).fetchone()[0]

def learnset(pid):
    return [m for (m,) in con.execute("SELECT move_id FROM PokemonLearnset WHERE pokemon_id=? ORDER BY move_id", (pid,))]

def learner(required=(), with_type=None, without_types=(), exclude=(), order="pokemon_id", need_noop=True, extra=None):
    used = {SPECIES[x] for x in exclude}
    for pid, t1, t2 in con.execute(f"SELECT pokemon_id, type1_id, type2_id FROM Pokemon WHERE is_selectable=1 ORDER BY {order}"):
        if SPECIES[pid] in used: continue
        types = {t1, t2}
        if with_type and TYPES[with_type] not in types: continue
        if any(TYPES[t] in types for t in without_types): continue
        ls = learnset(pid)
        if len(ls) < 4 or not all(m in ls for m in required): continue
        if need_noop and not any(m in NO_EFFECT for m in ls): continue
        if extra and not extra(ls): continue
        return pid
    if need_noop:   # 시간을 보내는 기술이 없어도 되는 경우가 많으므로 조건을 풀고 다시 찾는다.
        return learner(required, with_type, without_types, exclude, order, False, extra)
    raise RuntimeError(f"no learner {required} {with_type}")

def member(pid, first):
    moves = list(first)
    ls = learnset(pid)
    moves += [m for m in ls if m in NO_EFFECT and m not in moves][:1]
    moves += [m for m in ls if m not in moves]
    return {"PokemonId": pid, "MoveIds": moves[:4]}

def noop_slot(m): return next((i + 1 for i, x in enumerate(m["MoveIds"]) if x in NO_EFFECT), len(m["MoveIds"]))

def fillers(exclude):
    picks = []
    for _ in range(2):
        picks.append(learner(exclude=list(exclude) + picks, order="base_hp DESC"))
    return [member(x, []) for x in picks]

class Player:
    count = 0
    def __init__(self):
        Player.count += 1
        self.sock = socket.create_connection(("127.0.0.1", port))
        self.sock.settimeout(8.0)
        self.buf = b""
        self.state = None
        self.send(f"NAME t{Player.count}")
        self.read_until("NAME_ACCEPTED")

    def send(self, text): self.sock.sendall((text + "\n").encode())

    def read_until(self, prefixes):
        lines = []
        while True:
            while b"\n" in self.buf:
                line, self.buf = self.buf.split(b"\n", 1)
                line = line.decode("utf-8")
                lines.append(line)
                if line.startswith("BATTLE_STATE "):
                    self.state = json.loads(line[13:])
                if line.startswith(prefixes):
                    return lines
            self.buf += self.sock.recv(65536)

    def turn(self):
        lines = self.read_until(PROMPTS)
        return [l[11:] for l in lines if l.startswith("BATTLE_LOG ")], lines[-1]

    def me(self): return self.state["Team"][self.state["Active"]]["Pokemon"]
    def opp(self): return self.state["Opponent"]

def start(host_team, guest_team):
    host, guest = Player(), Player()
    host.send("1")
    room = host.read_until("CREATED")[-1].split()[1]
    guest.send(room); guest.read_until("JOINED")
    host.send("1"); guest.send("1")
    host.read_until("POKEMON_CATALOG"); guest.read_until("POKEMON_CATALOG")
    host.send("SELECT_TEAM " + json.dumps(host_team, separators=(",", ":")))
    guest.send("SELECT_TEAM " + json.dumps(guest_team, separators=(",", ":")))
    host.turn(); guest.turn()
    return host, guest

def battle(a, a_moves, d, d_moves):
    ht = [member(a, a_moves)] + fillers([a, d]); gt = [member(d, d_moves)] + fillers([a, d])
    host, guest = start(ht, gt)
    return host, guest, f"USE_MOVE {noop_slot(ht[0])}", f"USE_MOVE {noop_slot(gt[0])}"

def play(host, guest, h, g):
    if h: host.send(h)
    if g: guest.send(g)
    return host.turn(), guest.turn()

def try_move(pl, slot):
    """기술을 고른다. 제한에 걸려 ERROR가 오면 False (올바른 선택은 바로 오는 응답이 없다)"""
    pl.send(f"USE_MOVE {slot}")
    pl.sock.settimeout(0.2)
    try:
        while b"\n" not in pl.buf:
            pl.buf += pl.sock.recv(65536)
    except socket.timeout:
        pass
    finally:
        pl.sock.settimeout(8.0)
    if pl.buf.startswith(b"ERROR"):
        pl.buf = pl.buf.split(b"\n", 1)[1]
        return False
    return True

def own_fail(lines):
    """자기 쪽 기술 바로 뒤의 "하지만 실패했다" (상대 기술의 실패는 제외)"""
    return any("하지만 실패했다" in l and i > 0 and not lines[i - 1].startswith("상대 ") for i, l in enumerate(lines))
has = lambda lines, t: any(t in l for l in lines)
changes = lambda v: v.get("StatChanges") or []
plain = {m for (m,) in con.execute(
    "SELECT move_id FROM PokemonMove WHERE category!='변화' AND power BETWEEN 50 AND 80 AND accuracy=100 AND priority=0 "
    "AND is_multi_hit=0 AND move_id NOT IN (SELECT move_id FROM MoveStatusEffect) "
    "AND move_id NOT IN (SELECT move_id FROM MoveStatChange) AND move_id NOT IN (SELECT move_id FROM MoveMeta WHERE crit_rate>0 OR drain!=0 OR flinch_chance>0)")}




# ---- 교체·설치기·묶기 ----
learnable = {m for (m,) in con.execute("SELECT DISTINCT move_id FROM PokemonLearnset")}
TYPE_SET = {pid: {t1, t2} for pid, t1, t2 in con.execute("SELECT pokemon_id, type1_id, type2_id FROM Pokemon")}
is_type = lambda pid, t: TYPES[t] in TYPE_SET[pid]
active_of = lambda pl: pl.state["Active"]
hp_of = lambda pl, i: pl.state["Team"][i]["Pokemon"]
GROUNDED_BENCH = dict(without_types=["비행"], order="base_hp DESC")

def team_with_bench(pid, moves, bench_kw=None, exclude=()):
    """pid가 앞에 서고, 뒤의 두 마리는 bench_kw 조건(기본: 땅에 있는 포켓몬)으로 고른다."""
    kw = bench_kw or GROUNDED_BENCH
    b1 = learner(exclude=[pid, *exclude], **kw)
    b2 = learner(exclude=[pid, b1, *exclude], **kw)
    return [member(pid, moves), member(b1, []), member(b2, [])]

# 1. 설치기
p("\n[1] 설치기")
a = learner([191]); d = learner(exclude=[a], **GROUNDED_BENCH)
ht = [member(a, [191])] + fillers([a, d]); gt = team_with_bench(d, [], exclude=[a])
host, guest = start(ht, gt); hnoop, gnoop = f"USE_MOVE {noop_slot(ht[0])}", f"USE_MOVE {noop_slot(gt[0])}"
logs = [play(host, guest, "USE_MOVE 1", gnoop)[0][0] for _ in range(4)]
check(sum(has(l, "압정이 흩어졌다") for l in logs) == 3 and has(logs[3], "하지만 실패했다"), "압정뿌리기 3층까지, 4번째는 실패")
(hl, _), (gl, _) = play(host, guest, hnoop, "SWITCH 2")
me = hp_of(guest, 1)
check(has(gl, "압정뿌리기의 데미지") and me["CurrentHp"] == me["MaxHp"] - me["MaxHp"] // 4, f"3층이면 1/4 데미지 ({me['CurrentHp']}/{me['MaxHp']})")

a = learner([446]); d = learner(exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [446], d, [])
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(has(hl, "뾰족한 바위가 떠다니기"), "스텔스록 설치")
(hl, _), (gl, _) = play(host, guest, hnoop, "SWITCH 2")
check(has(gl, "뾰족한 바위가") and has(gl, "박혔다") and hp_of(guest, 1)["CurrentHp"] < hp_of(guest, 1)["MaxHp"], "교체해서 나오면 스텔스록 데미지")

a = learner([390]); d = learner(exclude=[a], **GROUNDED_BENCH)
bench_kw = dict(without_types=["비행", "독", "강철"], order="base_hp DESC")
ht = [member(a, [390])] + fillers([a, d]); gt = team_with_bench(d, [], bench_kw, exclude=[a])
host, guest = start(ht, gt); hnoop, gnoop = f"USE_MOVE {noop_slot(ht[0])}", f"USE_MOVE {noop_slot(gt[0])}"
play(host, guest, "USE_MOVE 1", gnoop); play(host, guest, "USE_MOVE 1", gnoop)
(hl, _), (gl, _) = play(host, guest, hnoop, "SWITCH 2")
check(hp_of(guest, 1)["Status"] == "맹독", f"독압정 2층이면 맹독 ({hp_of(guest, 1)['Status']})")

if 564 in learnable:
    a = learner([564]); d = learner(exclude=[a], **GROUNDED_BENCH)
    ht = [member(a, [564])] + fillers([a, d]); gt = team_with_bench(d, [], exclude=[a])
    host, guest = start(ht, gt); hnoop, gnoop = f"USE_MOVE {noop_slot(ht[0])}", f"USE_MOVE {noop_slot(gt[0])}"
    play(host, guest, "USE_MOVE 1", gnoop)
    (hl, _), (gl, _) = play(host, guest, hnoop, "SWITCH 2")
    check(changes(guest.me()) == ["스피드 -1"], f"끈적끈적네트로 스피드 -1 ({changes(guest.me())})")

a = learner([446]); d = learner([229], exclude=[a], order="base_hp DESC", without_types=["고스트"])
host, guest, hnoop, gnoop = battle(a, [446], d, [229])
play(host, guest, "USE_MOVE 1", gnoop)
(hl, _), (gl, _) = play(host, guest, hnoop, "USE_MOVE 1")
p("  ", gl)
check(has(gl, "설치기가 사라졌다"), "고속스핀으로 설치기 제거")
(hl, _), (gl, _) = play(host, guest, hnoop, "SWITCH 2")
check(not has(gl, "박혔다"), "제거한 뒤에는 스텔스록 데미지 없음")

# 2. 강제 교체
p("\n[2] 강제 교체")
a = learner([46]); d = learner(exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [46], d, [])
(hl, _), (gl, _) = play(host, guest, "USE_MOVE 1", gnoop)
check(has(gl, "전투에 끌려 나왔다") and active_of(guest) != 0, f"울부짖기로 강제 교체 (나온 번호 {active_of(guest)})")

dt = next(m for m in (525, 509) if m in learnable)
a = learner([dt]); d = learner(exclude=[a], order="base_hp DESC", without_types=["페어리", "고스트"])
host, guest, hnoop, gnoop = battle(a, [dt], d, [])
for _ in range(4):
    (hl, _), (gl, _) = play(host, guest, "USE_MOVE 1", gnoop)
    if not has(hl, "빗나갔다"): break
check(has(gl, "전투에 끌려 나왔다") and active_of(guest) != 0, f"{name(dt)}로 강제 교체")

# 3. 교체 막기·묶기
p("\n[3] 교체 막기·묶기")
a = learner([212]); d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트"])
host, guest, hnoop, gnoop = battle(a, [212], d, [])
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(has(hl, "도망칠 수 없다"), "검은눈빛")
guest.send("SWITCH 2")
err = guest.read_until("ERROR")[-1]
check("붙잡혀" in err, f"교체 불가 ({err})")
ghost = learner(with_type="고스트", exclude=[a])
host, guest, hnoop, gnoop = battle(a, [212], ghost, [])
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(own_fail(hl), "고스트 타입에게는 실패")

bind = next(m for m in (35, 20, 83, 328, 611) if m in learnable)
a = learner([bind], order="base_hp DESC"); d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트", "바위", "강철", "물", "불꽃"])
host, guest, hnoop, gnoop = battle(a, [bind], d, [])
for _ in range(5):
    (hl, _), (gl, _) = play(host, guest, "USE_MOVE 1", gnoop)
    if has(gl, "붙잡혔다"): break
p("  ", gl)
check(has(gl, "붙잡혔다") and has(gl, "의 데미지를 입었다"), f"{name(bind)}: 붙잡고 턴 끝 데미지")
guest.send("SWITCH 2")
err = guest.read_until("ERROR")[-1]
check("붙잡혀" in err, f"묶인 동안 교체 불가 ({err})")
ticks, freed = 0, False
for _ in range(6):
    (hl, _), (gl, _) = play(host, guest, hnoop, gnoop)
    ticks += has(gl, f"{name(bind)}의 데미지"); freed |= has(gl, "풀려났다")
    if freed: break
check(freed and 2 <= ticks + 1 <= 5, f"4~5번 데미지 뒤 풀려남 (데미지 {ticks + 1}번)")

# 4. 턴 도중 교체: 유턴·볼트체인지·배턴터치·막말내뱉기
p("\n[4] 턴 도중 교체")
ut = next(m for m in (369, 521) if m in learnable)
a = learner([ut]); d = learner(exclude=[a], order="base_hp DESC", without_types=["땅", "고스트"])
host, guest, hnoop, gnoop = battle(a, [ut], d, [])
host.send("USE_MOVE 1"); guest.send(gnoop)
hl, hp = host.turn(); gl, gp = guest.turn()
check(hp == "CHOOSE_SWITCH_MIDTURN" and gp == "WAIT_SWITCH", f"{name(ut)} 뒤 교체 선택 ({hp}, {gp})")
host.send("SWITCH 3")
hl, hp = host.turn(); gl, gp = guest.turn()
check(active_of(host) == 2 and has(hl, "돌아와") and hp == "CHOOSE_ACTION", f"고른 포켓몬으로 교체 후 턴 진행 ({hp})")

if 521 in learnable:
    a = learner([521]); d = learner(with_type="땅", exclude=[a], order="base_hp DESC")
    host, guest, hnoop, gnoop = battle(a, [521], d, [])
    (hl, hp), _ = play(host, guest, "USE_MOVE 1", gnoop)
    check(hp == "CHOOSE_ACTION" and active_of(host) == 0, "볼트체인지가 땅 타입에게 효과가 없으면 교체하지 않음")

a = learner([14, 226]); d = learner(exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [14, 226], d, [])
play(host, guest, "USE_MOVE 1", gnoop)
host.send("USE_MOVE 2"); guest.send(gnoop)
hl, hp = host.turn(); guest.turn()
check(hp == "CHOOSE_SWITCH_MIDTURN", f"배턴터치 뒤 교체 선택 ({hp})")
host.send("SWITCH 2"); host.turn(); guest.turn()
check(active_of(host) == 1 and changes(host.me()) == ["공격 +2"], f"배턴터치로 랭크 전달 ({changes(host.me())})")

if 575 in learnable:
    a = learner([575]); d = learner(exclude=[a], order="base_hp DESC")
    host, guest, hnoop, gnoop = battle(a, [575], d, [])
    host.send("USE_MOVE 1"); guest.send(gnoop)
    hl, hp = host.turn(); guest.turn()
    check(hp == "CHOOSE_SWITCH_MIDTURN" and sorted(changes(host.opp())) == ["공격 -1", "특수공격 -1"],
          f"막말내뱉기: 상대 공격·특공 -1 후 교체 ({changes(host.opp())})")
    host.send("SWITCH 2"); host.turn(); guest.turn()

# 5. 치유소원
p("\n[5] 치유소원·초승달춤")
hw = next(m for m in (361, 461) if m in learnable)
a = learner([hw]); d = learner(exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [hw], d, [])
(hl, hp), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(hp == "CHOOSE_SWITCH" and hp_of(host, 0)["CurrentHp"] == 0, f"{name(hw)}을 쓰면 자신이 쓰러짐")
host.send("SWITCH 2"); hl, hp = host.turn(); guest.turn()
check(has(hl, "체력과 상태이상이 회복되었다"), f"다음 포켓몬 회복 ({hl[-2:]})")

# 6. 무작위 대전 (유턴·배턴터치 포함)
p("\n[6] 무작위 대전")
import random
pool = [pid for (pid,) in con.execute("SELECT pokemon_id FROM Pokemon WHERE is_selectable=1")]
pivots = [m for m in (369, 521, 226, 575, 46, 18, 525, 446, 191) if m in learnable]
ended = 0
for game in range(8):
    picks = []
    while len(picks) < 6:
        x = random.choice(pool)
        if SPECIES[x] not in {SPECIES[y] for y in picks} and len(learnset(x)) >= 5: picks.append(x)
    def moves_of(x):
        ls = learnset(x); first = [m for m in ls if m in pivots][:2]
        return first + random.sample([m for m in ls if m not in first], 4 - len(first))
    team_of = lambda pids: [{"PokemonId": x, "MoveIds": moves_of(x)} for x in pids]
    host, guest = start(team_of(picks[:3]), team_of(picks[3:]))
    prompts = {host: "CHOOSE_ACTION", guest: "CHOOSE_ACTION"}
    for _ in range(150):
        if any(v.startswith("BATTLE_END") for v in prompts.values()): break
        acted = []
        for pl in (host, guest):
            if prompts[pl] == "CHOOSE_ACTION":
                for slot in random.sample([1, 2, 3, 4], 4):
                    if try_move(pl, slot): break
            elif prompts[pl] in ("CHOOSE_SWITCH", "CHOOSE_SWITCH_MIDTURN"):
                alive = [i for i, m in enumerate(pl.state["Team"]) if m["Pokemon"]["CurrentHp"] > 0 and i != pl.state["Active"]]
                pl.send(f"SWITCH {alive[0] + 1}")
        for pl in (host, guest):
            _, prompts[pl] = pl.turn()
    ended += any(v.startswith("BATTLE_END") for v in prompts.values())
    p(f"   {game + 1}판: {list(prompts.values())}")
check(ended >= 6, f"무작위 대전 8판 중 {ended}판이 끝까지 진행")

server.kill()
p("\n총 실패:", len(failures))
sys.exit(1 if failures else 0)
