# 6세대 규칙 확인 테스트. 사용: python -I attack_test.py <Server.exe> <Pokemon.db> <결과 파일> <서버 코드 폴더>
import glob, io, json, os, re, socket, sqlite3, subprocess, sys, time

exe, db, out_path, code_dir = sys.argv[1:5]
out = io.open(out_path, "w", encoding="utf-8")
port = 5075
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


# ---- 공격기 추가 효과 ----
STAT_KO = {"attack": "공격", "defense": "방어", "special-attack": "특수공격", "special-defense": "특수방어",
           "speed": "스피드", "accuracy": "명중률", "evasion": "회피율"}
learnable = {m for (m,) in con.execute("SELECT DISTINCT move_id FROM PokemonLearnset")}
acc100 = {m for (m,) in con.execute("SELECT move_id FROM PokemonMove WHERE accuracy=100 OR accuracy IS NULL")}
first_learnable = lambda ids: next((m for m in ids if m in learnable), None)
half_up = lambda x: int(x + 0.5)

def use_until_hit(host, guest, h, g, tries=8):
    for _ in range(tries):
        (hl, hp), (gl, gp) = play(host, guest, h, g)
        if not has(hl, "빗나갔다"): return hl, gl
    return hl, gl

# 1. 급소: 업어후리기·얼음숨결은 반드시 급소, 기충전 + 급소 잘 맞는 기술도 반드시 급소(랭크 3)
p("\n[1] 급소 랭크")
always = first_learnable([480, 524])
a = learner([always]); d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트"])
host, guest, hnoop, gnoop = battle(a, [always], d, [])
crits = total = 0
for _ in range(5):
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    for l in hl:
        if name(always) in l and "데미지)" in l:
            total += 1; crits += "급소에 맞았다" in l
    if host.opp()["CurrentHp"] == 0: break
check(total > 0 and crits == total, f"{name(always)} 항상 급소 ({crits}/{total})")

high = {m for (m,) in con.execute("SELECT move_id FROM MoveMeta WHERE crit_rate=1")} & acc100
a = learner([116], extra=lambda ls: any(m in high for m in ls))
hc = next(m for m in learnset(a) if m in high)
d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트", "노말", "강철", "비행", "악", "페어리"])
host, guest, hnoop, gnoop = battle(a, [116, hc], d, [])
play(host, guest, "USE_MOVE 1", gnoop)
crits = total = 0
for _ in range(5):
    (hl, _), _ = play(host, guest, "USE_MOVE 2", gnoop)
    for l in hl:
        if name(hc) in l and "데미지)" in l:
            total += 1; crits += "급소에 맞았다" in l
    if host.opp()["CurrentHp"] == 0: break
check(total > 0 and crits == total, f"기충전 + {name(hc)} 항상 급소 ({crits}/{total})")

# 2. 연속기: 2~5회 분포, 2회 고정, 데미지 합 = HP 감소
p("\n[2] 연속기")
multi25 = [m for (m,) in con.execute("SELECT move_id FROM MoveMeta WHERE min_hits=2 AND max_hits=5")]
mv = next(m for m in multi25 if m in learnable and m in acc100)
a = learner([mv]); d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트"])
counts, sums_ok = [], 0
while len(counts) < 40:
    host, guest, hnoop, gnoop = battle(a, [mv], d, [])
    for _ in range(6):
        before = host.opp()["CurrentHp"]
        (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
        line = next((l for l in hl if "번 맞았다" in l), None)
        if line is None: continue
        m = re.search(r"(\d)번 맞았다! \((.+) 데미지\)", line)
        n = int(m.group(1)); parts = [int(x.replace("급소 ", "")) for x in m.group(2).split(" + ")]
        if host.opp()["CurrentHp"] > 0:
            counts.append(n)
            sums_ok += (before - host.opp()["CurrentHp"]) == sum(parts) and len(parts) == n
        if host.opp()["CurrentHp"] < host.opp()["MaxHp"] // 3: break
mean = sum(counts) / len(counts)
p(f"   {name(mv)} 횟수 분포 " + str({k: counts.count(k) for k in range(2, 6)}))
check(all(2 <= c <= 5 for c in counts), "2~5회 범위")
check(2.7 <= mean <= 3.65, f"평균 약 3.17회 ({mean:.2f})")
check(sums_ok == len(counts), f"타격 데미지 합 = HP 감소 ({sums_ok}/{len(counts)})")

two = first_learnable([24, 458, 530, 155, 544])
a = learner([two]); d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트", "땅"])
host, guest, hnoop, gnoop = battle(a, [two], d, [])
seen = []
for _ in range(4):
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    if host.opp()["CurrentHp"] == 0: break
    seen += [l for l in hl if "번 맞았다" in l]
p("  ", seen)
check(seen and all(l.startswith("2번 맞았다") for l in seen), f"{name(two)} 2회 고정")

# 3. 트리플킥: 최대 3회, 위력 10→20→30
p("\n[3] 트리플킥")
if 167 in learnable:
    a = learner([167]); d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트"])
    rising = total3 = 0; ns = []
    for _ in range(3):
        host, guest, hnoop, gnoop = battle(a, [167], d, [])
        for _ in range(5):
            (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
            line = next((l for l in hl if "번 맞았다" in l), None)
            if line is None: continue
            m = re.search(r"(\d)번 맞았다! \((.+) 데미지\)", line)
            ns.append(int(m.group(1)))
            if int(m.group(1)) == 3 and "급소" not in line:
                x = [int(v) for v in m.group(2).split(" + ")]
                total3 += 1; rising += x[0] < x[1] < x[2]
            if host.opp()["CurrentHp"] < host.opp()["MaxHp"] // 3: break
    p(f"   횟수 {ns}")
    check(all(1 <= n <= 3 for n in ns), "1~3회")
    check(total3 == 0 or rising / total3 >= 0.8, f"3회 맞으면 데미지가 커진다 ({rising}/{total3})")

# 4. 흡수: 준 데미지의 절반 회복
p("\n[4] 흡수")
drain = first_learnable([202, 72, 409, 141, 71])
a = learner([drain]); d = learner(exclude=[a], order="base_attack DESC", without_types=["고스트"],
                                  extra=lambda ls: any(m in plain for m in ls))
dhit = next(m for m in learnset(d) if m in plain)
ok = tried = 0
for _ in range(4):
    host, guest, hnoop, gnoop = battle(a, [drain], d, [dhit])
    play(host, guest, hnoop, "USE_MOVE 1")
    if host.me()["CurrentHp"] == 0: continue
    missing = host.me()["MaxHp"] - host.me()["CurrentHp"]
    before = host.opp()["CurrentHp"]
    hl, _ = use_until_hit(host, guest, "USE_MOVE 1", gnoop)
    dealt = before - host.opp()["CurrentHp"]
    heal = re.search(r"체력을 회복했다! \(\+(\d+)\)", " ".join(hl))
    expected = min(max(1, half_up(dealt * 0.5)), missing)
    tried += 1
    ok += (heal and int(heal.group(1)) == expected) or (missing == 0 and heal is None)
    p(f"   데미지 {dealt}, 회복 {heal.group(1) if heal else None}, 기대 {expected}")
check(tried > 0 and ok == tried, f"{name(drain)} 회복량 ({ok}/{tried})")

# 5. 꿈먹기: 상대가 깨어 있으면 실패
p("\n[5] 꿈먹기")
if 138 in learnable:
    a = learner([138]); d = learner(exclude=[a], order="base_hp DESC")
    host, guest, hnoop, gnoop = battle(a, [138], d, [])
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    check(own_fail(hl) and host.opp()["CurrentHp"] == host.opp()["MaxHp"], "깨어 있는 상대에게 꿈먹기 실패")

# 6. 반동: 준 데미지의 33%
p("\n[6] 반동")
recoil = first_learnable([38, 413, 452, 394, 344])
pct = -con.execute("SELECT drain FROM MoveMeta WHERE move_id=?", (recoil,)).fetchone()[0]
a = learner([recoil]); d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트", "강철", "바위"])
ok = tried = 0
for _ in range(3):
    host, guest, hnoop, gnoop = battle(a, [recoil], d, [])
    hp0, before = host.me()["CurrentHp"], host.opp()["CurrentHp"]
    hl, _ = use_until_hit(host, guest, "USE_MOVE 1", gnoop)
    dealt = before - host.opp()["CurrentHp"]
    lost = hp0 - host.me()["CurrentHp"]
    expected = min(max(1, half_up(dealt * pct / 100)), hp0)
    tried += 1; ok += lost == expected and has(hl, "반동으로 데미지를 입었다")
    p(f"   데미지 {dealt}, 반동 {lost}, 기대 {expected}")
check(ok == tried, f"{name(recoil)} 반동 {pct}% ({ok}/{tried})")

# 7. 속이기: 나온 뒤 첫 행동만 성공, 상대가 풀죽음
p("\n[7] 속이기·풀죽음")
a = learner([252]); d = learner(exclude=[a], without_types=["고스트"], extra=lambda ls: any(m in plain for m in ls))
dhit = next(m for m in learnset(d) if m in plain)
host, guest, hnoop, gnoop = battle(a, [252], d, [dhit])
(hl, _), (gl, _) = play(host, guest, "USE_MOVE 1", "USE_MOVE 1")
check(has(gl, "풀이 죽어 기술을 쓸 수 없다") and not has(gl, name(dhit) + "!"), "첫 턴 속이기 → 상대 풀죽음")
(hl, _), (gl, _) = play(host, guest, "USE_MOVE 1", gnoop)
check(own_fail(hl), "두 번째 턴 속이기 실패")
play(host, guest, "SWITCH 2", gnoop); play(host, guest, "SWITCH 1", gnoop)
(hl, _), (gl, _) = play(host, guest, "USE_MOVE 1", "USE_MOVE 1")
check(has(gl, "풀이 죽어"), "다시 나오면 속이기 성공")
(hl, _), (gl, _) = play(host, guest, hnoop, "USE_MOVE 1")
check(not has(gl, "풀이 죽어"), "풀죽음은 다음 턴으로 넘어가지 않음")

# 8. 능력치 변화: 상대 100% 하락, 자신 100% 상승, 자신 하락은 대타가 맞아도 적용
p("\n[8] 능력치 변화 추가 효과")
def stat_text(move_id, target):
    rows = con.execute("SELECT stat, change FROM MoveStatChange WHERE move_id=? AND target=?", (move_id, target)).fetchall()
    return sorted(f"{STAT_KO[s]} {c:+d}" for s, c in rows)
drop = first_learnable([490, 522, 595, 491, 196, 317, 341])
a = learner([drop]); d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트", "강철", "비행"])
host, guest, hnoop, gnoop = battle(a, [drop], d, [])
use_until_hit(host, guest, "USE_MOVE 1", gnoop)
check(sorted(changes(host.opp())) == stat_text(drop, "opponent"), f"{name(drop)}: {changes(host.opp())} (기대 {stat_text(drop, 'opponent')})")

rise = first_learnable([488, 612])
a = learner([rise]); d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트", "물", "바위", "불꽃", "드래곤"])
host, guest, hnoop, gnoop = battle(a, [rise], d, [])
use_until_hit(host, guest, "USE_MOVE 1", gnoop)
check(sorted(changes(host.me())) == stat_text(rise, "self"), f"{name(rise)}: {changes(host.me())}")

cc = first_learnable([370, 276])
a = learner([cc]); d = learner([164], exclude=[a], order="base_hp DESC", without_types=["고스트"])
host, guest, hnoop, gnoop = battle(a, [cc], d, [164])
play(host, guest, hnoop, "USE_MOVE 1")
hp_before = host.opp()["CurrentHp"]
hl, _ = use_until_hit(host, guest, "USE_MOVE 1", gnoop)
p("  ", hl)
check(has(hl, "대타가 공격을 대신") and host.opp()["CurrentHp"] == hp_before, f"{name(cc)}가 대타에 맞음")
check(sorted(changes(host.me())) == stat_text(cc, "self"), f"대타에 맞아도 자신 능력치 하락: {changes(host.me())}")

if 229 in learnable:
    a = learner([229]); d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트"])
    host, guest, hnoop, gnoop = battle(a, [229], d, [])
    play(host, guest, "USE_MOVE 1", gnoop)
    check(changes(host.me()) == [], f"고속스핀은 6세대라 스피드가 오르지 않음 ({changes(host.me())})")

# 9. 흰안개가 추가 효과의 능력치 하락을 막는다
p("\n[9] 흰안개")
if 54 in learnable:
    a = learner([drop]); d = learner([54], exclude=[a], order="base_hp DESC", without_types=["고스트", "강철", "비행"])
    host, guest, hnoop, gnoop = battle(a, [drop], d, [54])
    play(host, guest, hnoop, "USE_MOVE 1")
    use_until_hit(host, guest, "USE_MOVE 1", gnoop)
    check(changes(host.opp()) == [], f"흰안개 중에는 {name(drop)} 하락 없음 ({changes(host.opp())})")

# 10. 코골기: 깨어 있으면 실패, 잠들어 있으면 공격
p("\n[10] 코골기")
if 173 in learnable and 147 in learnable:
    a = learner([173], without_types=["풀"]); d = learner([147], exclude=[a], order="base_hp DESC")
    host, guest, hnoop, gnoop = battle(a, [173], d, [147])
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    check(own_fail(hl), "깨어 있을 때 코골기 실패")
    play(host, guest, hnoop, "USE_MOVE 1")
    check(host.me()["Status"] == "잠듦", "버섯포자로 잠듦")
    before = host.opp()["CurrentHp"]
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    p("  ", hl)
    check(has(hl, "쿨쿨") and host.opp()["CurrentHp"] < before, "잠든 채로 코골기 공격")

# 11. 무작위 기술로 끝까지 (예외·멈춤 없는지)
p("\n[11] 무작위 대전")
import random
pool = [pid for (pid,) in con.execute("SELECT pokemon_id FROM Pokemon WHERE is_selectable=1")]
ended = 0
for game in range(4):
    picks = []
    while len(picks) < 6:
        x = random.choice(pool)
        if SPECIES[x] not in {SPECIES[y] for y in picks} and len(learnset(x)) >= 5: picks.append(x)
    # 배턴터치(226)는 내보낼 포켓몬을 따로 골라야 해서 뺀다.
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
check(ended >= 2, f"무작위 대전 4판 중 {ended}판이 끝까지 진행 (나머지는 150턴 제한)")

server.kill()
p("\n총 실패:", len(failures))
sys.exit(1 if failures else 0)
