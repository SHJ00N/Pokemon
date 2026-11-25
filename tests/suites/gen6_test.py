# 6세대 규칙 확인 테스트. 사용: python -I gen6_test.py <Server.exe> <Pokemon.db> <결과 파일> <서버 코드 폴더>
import glob, io, json, os, re, socket, sqlite3, subprocess, sys, time

exe, db, out_path, code_dir = sys.argv[1:5]
out = io.open(out_path, "w", encoding="utf-8")
port = 5071
server = subprocess.Popen([exe, str(port)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
import atexit; atexit.register(server.kill)
time.sleep(3)
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

# 1. 급소 1.5배, 기충전 1/2
p("\n[1] 급소")
a = learner([116], extra=lambda ls: any(m in plain for m in ls))
hit = next(m for m in learnset(a) if m in plain)
d = learner(exclude=[a], order="base_hp DESC")
crit, normal = [], []
for _ in range(8):
    host, guest, hnoop, gnoop = battle(a, [116, hit], d, [])
    play(host, guest, "USE_MOVE 1", gnoop)
    for _ in range(8):
        (hl, _), _ = play(host, guest, "USE_MOVE 2", gnoop)
        for l in hl:
            m = re.search(r"\((\d+) 데미지\)", l)
            if m and name(hit) in l:
                (crit if "급소" in l else normal).append(int(m.group(1)))
        if host.opp()["CurrentHp"] < host.opp()["MaxHp"] // 3: break
rate = len(crit) / max(1, len(crit) + len(normal))
ratio = (sum(crit) / len(crit)) / (sum(normal) / len(normal)) if crit and normal else 0
p(f"   급소 {len(crit)}번 / 전체 {len(crit) + len(normal)}번, 평균 데미지 비율 {ratio:.2f}")
check(0.25 <= rate <= 0.75, f"기충전 급소율 약 1/2 ({rate:.2f})")
check(1.3 <= ratio <= 1.7, f"급소 데미지 약 1.5배 ({ratio:.2f})")

# 2. 방어 연속 1/3, 패스트가드는 연속으로 써도 성공
p("\n[2] 방어 연속 사용")
a = learner([182]); d = learner(exclude=[a], order="base_hp DESC")
second = 0
for i in range(18):
    if i % 4 == 0:   # 방어 PP 10: 4번마다 새 전투
        host, guest, hnoop, gnoop = battle(a, [182], d, [])
    play(host, guest, "USE_MOVE 1", gnoop)
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    second += has(hl, "방어 태세")
    play(host, guest, hnoop, gnoop)
check(1 <= second <= 11, f"연속 두 번째 방어 성공률 약 1/3 ({second}/18)")
a = learner([501]); d = learner(exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [501], d, [])
ok = 0
for _ in range(8):
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    ok += has(hl, "패스트가드로 보호")
check(ok == 8, f"패스트가드는 연속으로 써도 성공 ({ok}/8)")

# 3. 전기 타입은 마비 면역
p("\n[3] 전기 타입 마비 면역")
a = learner([86]); d = learner(with_type="전기", exclude=[a])
host, guest, hnoop, gnoop = battle(a, [86], d, [])
for _ in range(8):
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    if not has(hl, "빗나갔다"): break
p("  ", hl)
check(own_fail(hl) and host.opp()["Status"] is None, "전기자석파가 전기 타입에게 실패")

# 4. 풀 타입은 가루·포자 면역
p("\n[4] 풀 타입 가루 면역")
powder = next(m for m in (147, 79, 78, 77) if any(True for _ in [0]) and m in {x for (x,) in con.execute("SELECT move_id FROM PokemonLearnset")})
a = learner([powder]); d = learner(with_type="풀", exclude=[a])
host, guest, hnoop, gnoop = battle(a, [powder], d, [])
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
p("  ", hl)
check(has(hl, "효과가 없는 것 같다") and host.opp()["Status"] is None, f"{name(powder)}가 풀 타입에게 효과 없음")

# 5. 달콤한향기 -2, 실뿜기 -2
p("\n[5] 달콤한향기·실뿜기")
for move_id, expected in ((230, "회피율 -2"), (81, "스피드 -2")):
    a = learner([move_id]); d = learner(exclude=[a], order="base_hp DESC")
    host, guest, hnoop, gnoop = battle(a, [move_id], d, [])
    for _ in range(8):
        (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
        if not has(hl, "빗나갔다"): break
    check(changes(host.opp()) == [expected], f"{name(move_id)}: {changes(host.opp())} (기대 {expected})")

# 6. 잠듦: 교체해도 남은 턴이 그대로
p("\n[6] 잠듦 + 교체")
worst = 0
for _ in range(5):
    a = learner([147]); d = learner(exclude=[a], order="base_hp DESC", without_types=["풀"])
    host, guest, hnoop, gnoop = battle(a, [147], d, [])
    play(host, guest, "USE_MOVE 1", gnoop)
    if guest.me()["Status"] != "잠듦": continue
    asleep = 0
    (hl, _), (gl, _) = play(host, guest, hnoop, gnoop)
    asleep += sum("쿨쿨" in l for l in gl)
    if guest.state["Team"][0]["Pokemon"]["Status"] == "잠듦":
        play(host, guest, hnoop, "SWITCH 2")
        play(host, guest, hnoop, "SWITCH 1")
        for _ in range(5):
            (hl, _), (gl, _) = play(host, guest, hnoop, gnoop)
            asleep += sum("쿨쿨" in l for l in gl)
            if guest.me()["Status"] is None: break
    worst = max(worst, asleep)
check(1 <= worst <= 3, f"교체를 끼워도 최대 3번만 잠들어 있음 (가장 길었던 경우 {worst}번)")

# 7. 독 타입이 쓰는 맹독은 반드시 명중
p("\n[7] 독 타입 맹독")
a = learner([92], with_type="독"); d = learner(exclude=[a], order="base_hp DESC", without_types=["독", "강철"])
misses = 0
for _ in range(15):
    host, guest, hnoop, gnoop = battle(a, [92], d, [])
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    misses += has(hl, "빗나갔다")
check(misses == 0, f"독 타입의 맹독 15번 모두 명중 (빗나감 {misses})")

# 8. 소리 기술은 대타를 무시
p("\n[8] 소리 기술 vs 대타")
a = learner([45], extra=lambda ls: 304 in ls)
d = learner([164], exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [45, 304], d, [164])
play(host, guest, hnoop, "USE_MOVE 1")
check("대타" in changes(guest.me()), "상대가 대타출동")
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(changes(host.opp()).count("공격 -1") == 1, f"울음소리가 대타를 무시하고 공격 -1 ({changes(host.opp())})")
before = host.opp()["CurrentHp"]
(hl, _), _ = play(host, guest, "USE_MOVE 2", gnoop)
p("  ", hl)
check(host.opp()["CurrentHp"] < before and not has(hl, "대타가 공격을 대신"), "하이퍼보이스가 대타를 무시하고 포켓몬에게 맞음")

# 9. 처음부터 끝까지 한 판 (멈추지 않는지)
p("\n[9] 한 판 끝까지")
# 6마리 모두 1번 칸에 공격기를 둔다.
attackers = []
for _ in range(6):
    attackers.append(learner(exclude=attackers, extra=lambda ls: any(m in plain for m in ls), order="base_attack DESC"))
team_of = lambda pids: [member(x, [next(m for m in learnset(x) if m in plain)]) for x in pids]
ht, gt = team_of(attackers[:3]), team_of(attackers[3:])
host, guest = start(ht, gt)
prompts = {host: "CHOOSE_ACTION", guest: "CHOOSE_ACTION"}
for _ in range(80):
    if any(v.startswith("BATTLE_END") for v in prompts.values()): break
    for pl in (host, guest):
        if prompts[pl] == "CHOOSE_ACTION": pl.send("USE_MOVE 1")
        elif prompts[pl] in ("CHOOSE_SWITCH", "CHOOSE_SWITCH_MIDTURN"):
            alive = [i for i, m in enumerate(pl.state["Team"]) if m["Pokemon"]["CurrentHp"] > 0 and i != pl.state["Active"]]
            pl.send(f"SWITCH {alive[0] + 1}")
    for pl in (host, guest):
        _, prompts[pl] = pl.turn()
check(sorted(v.split()[1] for v in prompts.values()) in (["LOSE", "WIN"], ["DRAW", "DRAW"]), f"승패까지 진행: {list(prompts.values())}")

server.kill()
p("\n총 실패:", len(failures))
sys.exit(1 if failures else 0)
