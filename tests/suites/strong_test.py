# 상태이상 마무리 테스트. 사용: python -I strong_test.py <Server.exe> <Pokemon.db> <결과 파일> <서버 코드 폴더>
import glob, io, json, os, re, socket, sqlite3, subprocess, sys, time

exe, db, out_path, code_dir = sys.argv[1:5]
out = io.open(out_path, "w", encoding="utf-8")
port = 5087
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
NO_EFFECT |= {373, 511, 495, 478, 271, 415, 285, 272, 380, 388, 493, 494, 100, 180, 288, 516, 363, 597, 602}   # 항상 실패하는 우선도 0 기술
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




# ---- 너무 강하게 동작하던 공격기 ----
learnable = {m for (m,) in con.execute("SELECT DISTINCT move_id FROM PokemonLearnset")}
WEIGHT = dict(con.execute("SELECT pokemon_id, weight FROM Pokemon"))
plain100 = {m for m in plain if m not in (89, 57, 16, 239)}
prompt_of = lambda r: r[1]

def pick(required, **kw):
    return learner(required, **kw)

def fight(a, a_moves, d, d_moves):
    host, guest, hnoop, gnoop = battle(a, a_moves, d, d_moves)
    return host, guest, hnoop, gnoop

def attacker_with_plain(**kw):
    return learner(extra=lambda ls: any(m in plain100 for m in ls), **kw)

# 1. 자폭·대폭발: 쓰면 자신이 쓰러진다 (상대가 고스트라 효과가 없어도)
p("\n[1] 자폭·대폭발")
boom = next(m for m in (153, 120) if m in learnable)
a = pick([boom]); d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트"])
host, guest, hnoop, gnoop = fight(a, [boom], d, [])
(hl, hp), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(host.state["Team"][0]["Pokemon"]["CurrentHp"] == 0 and hp == "CHOOSE_SWITCH" and has(hl, "쓰러졌다"),
      f"{name(boom)}을 쓰면 자신이 쓰러짐 ({hp})")
d = learner(with_type="고스트", exclude=[a])
host, guest, hnoop, gnoop = fight(a, [boom], d, [])
(hl, hp), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(has(hl, "효과가 없는") and host.state["Team"][0]["Pokemon"]["CurrentHp"] == 0, "고스트에게 효과가 없어도 쓰러짐")

# 2. 파괴광선: 맞히면 다음 턴은 반동으로 쉰다 (효과가 없으면 쉬지 않음)
p("\n[2] 반동으로 쉬기")
beam = 63
a = pick([beam]); d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트", "바위", "강철"])
ok = False
for _ in range(4):
    host, guest, hnoop, gnoop = fight(a, [beam], d, [])
    (hl, hp), _ = play(host, guest, "USE_MOVE 1", gnoop)
    if has(hl, "빗나갔다"): continue
    check(hp == "LOCKED", f"파괴광선 다음 턴은 LOCKED ({hp})")
    (hl, hp), _ = play(host, guest, None, gnoop)
    p("  ", hl)
    check(has(hl, "반동으로 움직일 수 없다") and hp == "CHOOSE_ACTION", f"반동으로 쉰 뒤 다시 고름 ({hp})")
    ok = True
    break
check(ok, "파괴광선 명중")
d = learner(with_type="고스트", exclude=[a])
host, guest, hnoop, gnoop = fight(a, [beam], d, [])
(hl, hp), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(hp == "CHOOSE_ACTION", f"효과가 없으면 쉬지 않음 ({hp})")

# 3. 2턴 기술: 솔라빔·로켓박치기는 다음 턴 공격, 공중날기 중에는 상대 공격이 빗나감
p("\n[3] 2턴 기술")
a = pick([76]); d = learner(exclude=[a], order="base_hp DESC", without_types=["불꽃", "풀", "드래곤", "벌레", "독", "비행", "강철"])
host, guest, hnoop, gnoop = fight(a, [76], d, [])
(hl, hp), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(has(hl, "빛을 흡수했다") and hp == "LOCKED" and host.opp()["CurrentHp"] == host.opp()["MaxHp"], f"솔라빔 첫 턴은 모으기만 함 ({hp})")
(hl, hp), _ = play(host, guest, None, gnoop)
check(host.opp()["CurrentHp"] < host.opp()["MaxHp"] and hp == "CHOOSE_ACTION", "솔라빔 다음 턴에 공격")

if 130 in learnable:
    a = pick([130]); d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트"])
    host, guest, hnoop, gnoop = fight(a, [130], d, [])
    play(host, guest, "USE_MOVE 1", gnoop)
    check("방어 +1" in changes(host.me()), f"로켓박치기 첫 턴 방어 +1 ({changes(host.me())})")

fly = 19
a = pick([fly]); d = attacker_with_plain(exclude=[a], order="base_hp DESC", without_types=["전기", "바위", "강철"])
dhit = next(m for m in learnset(d) if m in plain100)
missed = 0
for _ in range(2):
    host, guest, hnoop, gnoop = fight(a, [fly], d, [dhit])
    (hl, hp), (gl, _) = play(host, guest, "USE_MOVE 1", "USE_MOVE 1")
    check(has(hl, "하늘 높이 날아올랐다") and hp == "LOCKED", "공중날기 첫 턴에 날아오름")
    (hl2, hp2), (gl2, _) = play(host, guest, None, "USE_MOVE 1")
    missed += sum("공격이 빗나갔다" in l and name(dhit) in l for l in gl + gl2)
    check(any(name(fly) + "!" in l and ("데미지)" in l or "빗나갔다" in l) for l in hl2), "공중날기 다음 턴에 공격 (명중률 95)")
check(missed == 2, f"날아오른 동안 상대 공격은 빗나감 ({missed}/2)")

# 4. 기습: 상대가 공격 기술을 골랐을 때만 성공
p("\n[4] 기습")
a = pick([389]); d = attacker_with_plain(exclude=[a], order="base_hp DESC", without_types=["악", "페어리", "격투"])
dhit = next(m for m in learnset(d) if m in plain100)
host, guest, hnoop, gnoop = fight(a, [389], d, [dhit])
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(own_fail(hl), "상대가 변화 기술이면 실패")
(hl, _), _ = play(host, guest, "USE_MOVE 1", "USE_MOVE 1")
check(any(name(389) in l and "데미지)" in l for l in hl), "상대가 공격하면 먼저 맞힘")

# 5. 힘껏펀치: 먼저 맞으면 실패
p("\n[5] 힘껏펀치")
a = pick([264], order="base_hp DESC"); d = attacker_with_plain(exclude=[a], without_types=["고스트", "비행", "에스퍼", "페어리"])
dhit = next(m for m in learnset(d) if m in plain100)
host, guest, hnoop, gnoop = fight(a, [264], d, [dhit])
(hl, _), _ = play(host, guest, "USE_MOVE 1", "USE_MOVE 1")
p("  ", hl)
check(has(hl, "기합을 모으기 시작했다") and has(hl, "기합이 흐트러져서"), "공격을 맞으면 기합이 흐트러짐")
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(any(name(264) in l and "데미지)" in l for l in hl), "맞지 않으면 성공")

# 6. 미래예지: 2턴 뒤 턴 끝에 맞는다, 예약 중에는 실패
p("\n[6] 미래예지")
fs = next(m for m in (248, 353) if m in learnable)
a = pick([fs]); d = learner(exclude=[a], order="base_hp DESC", without_types=["악", "강철", "에스퍼", "불꽃", "물"])
host, guest, hnoop, gnoop = fight(a, [fs], d, [])
full = lambda: host.opp()["CurrentHp"] == host.opp()["MaxHp"]
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(full(), "쓴 턴에는 맞지 않음")
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(own_fail(hl) and full(), "예약 중에 다시 쓰면 실패, 다음 턴에도 맞지 않음")
(hl, _), (gl, _) = play(host, guest, hnoop, gnoop)
p("  ", gl)
check(has(gl, "공격을 받았다") and not full(), "2턴 뒤 턴 끝에 맞음")

# 7. 점프킥·무릎차기: 막히면 최대 HP의 1/2 반동
p("\n[7] 빗나감 반동")
kick = next(m for m in (136, 26) if m in learnable)
a = pick([kick]); d = learner(with_type="고스트", exclude=[a])
host, guest, hnoop, gnoop = fight(a, [kick], d, [])
mx = host.me()["MaxHp"]
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(has(hl, "땅에 부딪혔다") and host.me()["CurrentHp"] == mx - mx // 2, f"고스트에게 막혀 1/2 반동 ({host.me()['CurrentHp']}/{mx})")

# 8. 비장의무기: 다른 기술을 모두 쓴 뒤에만 성공
p("\n[8] 비장의무기")
if 387 in learnable:
    a = pick([387]); d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트", "바위", "강철"])
    others = [m for m in learnset(a) if m in NO_EFFECT][:3]
    others += [m for m in learnset(a) if m not in others and m != 387][:3 - len(others)]
    ht = [{"PokemonId": a, "MoveIds": [387] + others}] + fillers([a, d]); gt = [member(d, [])] + fillers([a, d])
    host, guest = start(ht, gt)
    gnoop = f"USE_MOVE {noop_slot(gt[0])}"
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    check(own_fail(hl), "처음에는 실패")
    for s in (2, 3, 4):
        play(host, guest, f"USE_MOVE {s}", gnoop)
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    p("  ", hl)
    check(any(name(387) in l and "데미지)" in l for l in hl), "다른 기술을 모두 쓴 뒤 성공")

# 9. 프리폴: 상대를 데려가 행동·교체를 막고 다음 턴에 떨어뜨림
p("\n[9] 프리폴")
if 507 in learnable:
    a = pick([507], order="base_speed DESC")
    d = attacker_with_plain(exclude=[a], order="base_speed ASC", without_types=["비행", "고스트"])
    check(WEIGHT[d] < 2000, f"상대 몸무게 200kg 미만 ({WEIGHT[d] / 10}kg)")
    dhit = next(m for m in learnset(d) if m in plain100)
    host, guest, hnoop, gnoop = fight(a, [507], d, [dhit])
    (hl, hp), (gl, _) = play(host, guest, "USE_MOVE 1", "USE_MOVE 1")
    p("  ", gl)
    check(has(gl, "하늘로 데려갔다") and has(gl, "공중에 붙잡혀 있어서 움직일 수 없다") and hp == "LOCKED", "상대를 데려가 움직이지 못하게 함")
    guest.send("SWITCH 2")
    err = guest.read_until("ERROR")[-1]
    check("프리폴" in err, f"붙잡힌 동안 교체 불가 ({err})")
    (hl, hp), (gl, _) = play(host, guest, None, "USE_MOVE 1")
    check(host.opp()["CurrentHp"] < host.opp()["MaxHp"] and hp == "CHOOSE_ACTION", "다음 턴에 떨어뜨려 데미지")
    (hl, _), (gl, _) = play(host, guest, hnoop, "USE_MOVE 1")
    check(not has(gl, "붙잡혀"), "떨어진 뒤에는 다시 행동")

# 10. 무작위 대전
p("\n[10] 무작위 대전")
import random
pool = [pid for (pid,) in con.execute("SELECT pokemon_id FROM Pokemon WHERE is_selectable=1")]
ended = 0
for game in range(6):
    picks = []
    while len(picks) < 6:
        x = random.choice(pool)
        if SPECIES[x] not in {SPECIES[y] for y in picks} and len(learnset(x)) >= 5: picks.append(x)
    team_of = lambda pids: [{"PokemonId": x, "MoveIds": random.sample(learnset(x), 4)} for x in pids]
    host, guest = start(team_of(picks[:3]), team_of(picks[3:]))
    prompts = {host: "CHOOSE_ACTION", guest: "CHOOSE_ACTION"}
    for _ in range(150):
        if any(v.startswith("BATTLE_END") for v in prompts.values()): break
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
check(ended >= 4, f"무작위 대전 6판 중 {ended}판이 끝까지 진행")

server.kill()
p("\n총 실패:", len(failures))
sys.exit(1 if failures else 0)
