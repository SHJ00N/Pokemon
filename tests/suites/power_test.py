# 상태이상 마무리 테스트. 사용: python -I power_test.py <Server.exe> <Pokemon.db> <결과 파일> <서버 코드 폴더>
import glob, io, json, os, re, socket, sqlite3, subprocess, sys, time

exe, db, out_path, code_dir = sys.argv[1:5]
out = io.open(out_path, "w", encoding="utf-8")
port = 5082
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




# ---- 위력 조건·위력 없는 공격기 ----
learnable = {m for (m,) in con.execute("SELECT DISTINCT move_id FROM PokemonLearnset")}
TYPE_OF_MOVE = dict(con.execute("SELECT move_id, type_id FROM PokemonMove"))
TYPE_SET = {pid: {t1, t2} for pid, t1, t2 in con.execute("SELECT pokemon_id, type1_id, type2_id FROM Pokemon")}
EFF = {(a, d): m for a, d, m in con.execute("SELECT attack_type_id, defend_type_id, multiplier FROM TypeEffectiveness")}
def eff(move_id, pid):
    r = 1.0
    for t in TYPE_SET[pid]:
        if t is not None: r *= EFF[(TYPE_OF_MOVE[move_id], t)]
    return r
def damage_of(lines, move_id):
    for l in lines:
        m = re.search(r"\((\d+) 데미지\)", l)
        if m and name(move_id) + "!" in l and "급소" not in l and "번 맞았다" not in l: return int(m.group(1))
    return None
def neutral_target(move_id, exclude, **kw):
    return learner(exclude=exclude, order="base_hp DESC", extra=None, **kw) if False else \
        next(pid for (pid,) in con.execute("SELECT pokemon_id FROM Pokemon WHERE is_selectable=1 ORDER BY base_hp DESC")
             if eff(move_id, pid) == 1 and pid not in exclude and len(learnset(pid)) >= 4
             and any(m in NO_EFFECT for m in learnset(pid)) and SPECIES[pid] not in {SPECIES[x] for x in exclude})

GROUP5 = [265, 358, 279, 419, 362, 371, 372, 514, 228, 512, 558, 559, 205, 301, 210, 497, 284, 323, 500, 473, 540,
          548, 492, 498, 533, 560, 573, 485, 237, 290, 12, 32, 90, 329, 49, 82, 69, 101, 149, 162, 283, 515, 68, 243,
          368, 117, 67, 447, 484, 535, 175, 179, 378, 462, 360, 486, 217, 222, 216, 218, 386, 255, 251, 562]

# 1. 모든 기술이 "아직 구현되지 않은 기술"로 나오지 않는다
p("\n[1] 미구현 표시 없음")
bad, tried = [], 0
for m in GROUP5:
    if m not in learnable: continue
    a = learner([m]); d = neutral_target(m, [a])
    host, guest, hnoop, gnoop = battle(a, [m], d, [])
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    tried += 1
    if any(name(m) in l and "아직 구현되지 않은" in l for l in hl): bad.append(name(m))
check(tried >= 60 and not bad, f"{tried}개 사용, 미구현 표시 {bad}")

# 2. 정해진 데미지
p("\n[2] 정해진 데미지")
for m, expected in ((69, 50), (101, 50), (82, 40), (49, 20)):
    if m not in learnable: continue
    a = learner([m]); d = neutral_target(m, [a])
    host, guest, hnoop, gnoop = battle(a, [m], d, [])
    for _ in range(5):
        before = host.opp()["CurrentHp"]
        (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
        if not has(hl, "빗나갔다"): break
    check(before - host.opp()["CurrentHp"] == expected, f"{name(m)} {expected} 데미지 ({before - host.opp()['CurrentHp']})")
a = learner([162]); d = neutral_target(162, [a])
host, guest, hnoop, gnoop = battle(a, [162], d, [])
for _ in range(5):
    before = host.opp()["CurrentHp"]
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    if not has(hl, "빗나갔다"): break
check(before - host.opp()["CurrentHp"] == before // 2, "분노의앞니: 상대 HP의 절반")
a = learner([101]); d = learner(with_type="노말", exclude=[a], without_types=["고스트"])
host, guest, hnoop, gnoop = battle(a, [101], d, [])
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(has(hl, "효과가 없는"), "나이트헤드는 노말 타입에게 효과 없음")

# 3. 일격필살
p("\n[3] 일격필살")
ohko = next(m for m in (32, 12, 329, 90) if m in learnable)
a = learner([ohko]); d = neutral_target(ohko, [a])
kos = misses = 0
for _ in range(10):
    host, guest, hnoop, gnoop = battle(a, [ohko], d, [])
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    kos += has(hl, "일격필살") and host.opp()["CurrentHp"] == 0
    misses += has(hl, "빗나갔다")
check(kos + misses == 10 and kos >= 1, f"{name(ohko)}: 맞으면 한 번에 쓰러짐 ({kos}/10 명중)")

# 4. 카운터·미러코트·참기
p("\n[4] 되갚기")
for move, category in ((68, "물리"), (243, "특수")):
    if move not in learnable: continue
    a = learner([move], order="base_hp DESC")
    hits = {m for (m,) in con.execute("SELECT move_id FROM PokemonMove WHERE category=?", (category,))} & plain
    d = learner(exclude=[a], order="base_hp DESC", extra=lambda ls: any(m in hits and eff(m, a) > 0 for m in ls))
    dhit = next(m for m in learnset(d) if m in hits and eff(m, a) > 0)
    if eff(move, d) == 0: continue
    for _ in range(5):   # 급소면 damage_of가 None이므로 다시 한다.
        host, guest, hnoop, gnoop = battle(a, [move], d, [dhit])
        (hl, _), (gl, _) = play(host, guest, "USE_MOVE 1", "USE_MOVE 1")
        taken = damage_of(gl, dhit); back = damage_of(hl, move)
        if taken is not None: break
    p(f"   {name(dhit)} {taken} → {name(move)} {back}")
    check(taken is not None and back == 2 * min(taken, host.state["Team"][0]["Pokemon"]["MaxHp"]), f"{name(move)}: 받은 데미지 2배")
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    check(own_fail(hl), f"{name(move)}: 맞지 않았으면 실패")

if 117 in learnable:
    a = learner([117], order="base_hp DESC")
    d = learner(exclude=[a], order="base_hp DESC", extra=lambda ls: any(m in plain and eff(m, a) > 0 for m in ls), without_types=["고스트"])
    dhit = next(m for m in learnset(d) if m in plain and eff(m, a) > 0)
    host, guest, hnoop, gnoop = battle(a, [117], d, [dhit])
    hp_start = host.me()["CurrentHp"]
    (hl, hp), (gl, _) = play(host, guest, "USE_MOVE 1", "USE_MOVE 1")
    check(has(hl, "참기 시작했다") and hp == "LOCKED", "참기 시작 (LOCKED)")
    (hl, hp), (gl, _) = play(host, guest, None, "USE_MOVE 1")
    total = hp_start - host.me()["CurrentHp"]     # 참는 동안 실제로 받은 데미지
    (hl, hp), (gl, _) = play(host, guest, None, gnoop)
    p("  ", hl)
    check(damage_of(hl, 117) == 2 * total and hp in ("CHOOSE_ACTION", "WAIT_SWITCH"), f"3번째 턴에 받은 데미지 {total}의 2배")

# 5. 위력 2배 조건
p("\n[5] 위력 2배 조건")
def ratio(a, a_moves, d, d_moves, attack_slot, setup, label, expected, rounds=5):
    vals = []
    for _ in range(rounds):
        host, guest, hnoop, gnoop = battle(a, a_moves, d, d_moves)
        (hl, _), _ = play(host, guest, f"USE_MOVE {attack_slot}", gnoop); d1 = damage_of(hl, a_moves[attack_slot - 1])
        setup(host, guest, hnoop, gnoop)
        (hl, _), _ = play(host, guest, f"USE_MOVE {attack_slot}", gnoop); d2 = damage_of(hl, a_moves[attack_slot - 1])
        if d1 and d2: vals.append(d2 / d1)
    mean = sum(vals) / max(1, len(vals))
    p(f"   {label} {[round(v, 2) for v in vals]}")
    check(len(vals) >= 2 and abs(mean - expected) <= expected * 0.25, f"{label}: 평균 {mean:.2f} (기대 {expected})")

# 정신차리기: 마비된 상대 2배, 맞으면 마비가 풀림
if 265 in learnable:
    a = learner([265, 86]); d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트", "전기", "땅"])
    def paralyze(host, guest, hnoop, gnoop):
        for _ in range(5):
            play(host, guest, "USE_MOVE 2", gnoop)
            if host.opp()["Status"] == "마비": return
    ratio(a, [265, 86], d, [], 1, paralyze, "정신차리기", 2)
# 소금물: 상대 HP 절반 이하에서 2배
a = learner([362]); d = neutral_target(362, [a])
def halve(host, guest, hnoop, gnoop):
    while host.opp()["CurrentHp"] * 2 > host.opp()["MaxHp"]:
        play(host, guest, "USE_MOVE 1", gnoop)
ratio(a, [362], d, [], 1, halve, "소금물", 2, rounds=5)
# 속임수: 상대 공격 +2면 2배
a = learner([492]); d = learner([14], exclude=[a], order="base_hp DESC", without_types=["악", "격투", "페어리"])
ratio(a, [492], d, [14], 1, lambda h, g, hn, gn: play(h, g, hn, "USE_MOVE 1"), "속임수 (상대 칼춤 후)", 2)
# 사이코쇼크: 상대 특수방어가 올라도 그대로
if 473 in learnable:
    a = learner([473]); d = learner([133], exclude=[a], order="base_hp DESC", without_types=["악", "강철", "에스퍼"])
    ratio(a, [473], d, [133], 1, lambda h, g, hn, gn: play(h, g, hn, "USE_MOVE 1"), "사이코쇼크 (상대 망각술 후)", 1)
# 야금야금: 상대 방어가 올라도 그대로
if 498 in learnable:
    a = learner([498]); d = learner([334], exclude=[a], order="base_hp DESC", without_types=["고스트", "바위", "강철"])
    ratio(a, [498], d, [334], 1, lambda h, g, hn, gn: play(h, g, hn, "USE_MOVE 1"), "야금야금 (상대 철벽 후)", 1)
# 연속자르기: 이어서 맞히면 2배
a = learner([210]); d = neutral_target(210, [a])
ratio(a, [210], d, [], 1, lambda h, g, hn, gn: None, "연속자르기 2번째", 2)

# 6. 상성·타입
p("\n[6] 상성·타입")
a = learner([573]); d = learner(with_type="물", exclude=[a], order="base_hp DESC", without_types=["얼음", "강철", "불꽃"])
host, guest, hnoop, gnoop = battle(a, [573], d, [])
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(any(name(573) in l and "효과가 굉장했다" in l for l in hl), "프리즈드라이가 물 타입에게 효과가 굉장함")
if 560 in learnable:
    a = learner([560]); d = learner(with_type="풀", exclude=[a], order="base_hp DESC", without_types=["비행", "독", "에스퍼", "페어리", "벌레", "강철", "바위", "전기"])
    host, guest, hnoop, gnoop = battle(a, [560], d, [])
    for _ in range(4):
        (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
        if not has(hl, "빗나갔다"): break
    check(any(name(560) in l and "효과가 굉장했다" in l for l in hl), "플라잉프레스는 비행 상성도 곱함 (풀 타입에게 굉장)")
a = learner([237]); d = learner(with_type="에스퍼", exclude=[a], order="base_hp DESC", without_types=["악", "격투", "페어리"])
host, guest, hnoop, gnoop = battle(a, [237], d, [])
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(any(name(237) in l and "효과가 굉장했다" in l for l in hl), "잠재파워는 악 타입 (에스퍼에게 굉장)")
if 485 in learnable:
    a = learner([485]); d = next(pid for (pid,) in con.execute("SELECT pokemon_id FROM Pokemon WHERE is_selectable=1 ORDER BY base_hp DESC")
                              if not (TYPE_SET[pid] & TYPE_SET[a] - {None}) and any(m in NO_EFFECT for m in learnset(pid)) and SPECIES[pid] != SPECIES[a])
    host, guest, hnoop, gnoop = battle(a, [485], d, [])
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    check(has(hl, "효과가 없는"), "싱크로노이즈: 같은 타입이 없으면 효과 없음")

# 7. 그 밖의 동작
p("\n[7] 그 밖의 동작")
a = learner([205]); d = neutral_target(205, [a])
for _ in range(5):
    host, guest, hnoop, gnoop = battle(a, [205], d, [])
    (hl, hp), _ = play(host, guest, "USE_MOVE 1", gnoop)
    if not has(hl, "빗나갔다"): break
prompts, damages = [hp], [damage_of(hl, 205)]
for _ in range(5):
    if hp != "LOCKED": break
    (hl, hp), _ = play(host, guest, None, gnoop); prompts.append(hp); damages.append(damage_of(hl, 205))
p("  ", prompts, damages)
check(prompts[0] == "LOCKED" and len(prompts) <= 5 and prompts[-1] != "LOCKED", "구르기: 맞히면 이어서 사용(LOCKED), 최대 5번")
pairs = [(x, y) for x, y in zip(damages, damages[1:]) if x and y]
check(all(1.5 <= y / x <= 2.6 for x, y in pairs), f"구르기: 맞힐 때마다 약 2배 ({damages})")

a = learner([254, 255]); d = neutral_target(255, [a])
host, guest, hnoop, gnoop = battle(a, [254, 255], d, [])
play(host, guest, "USE_MOVE 1", gnoop); play(host, guest, "USE_MOVE 1", gnoop)
(hl, _), _ = play(host, guest, "USE_MOVE 2", gnoop)
check(any(name(255) in l and "데미지)" in l for l in hl) and changes(host.me()) == [], f"토해내기 후 비축 랭크가 원래대로 ({changes(host.me())})")
(hl, _), _ = play(host, guest, "USE_MOVE 2", gnoop)
check(own_fail(hl), "비축이 없으면 토해내기 실패")

a = learner([251]); d = neutral_target(251, [a])
host, guest, hnoop, gnoop = battle(a, [251], d, [])
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(has(hl, "3번 맞았다"), f"집단구타: 건강한 팀 3마리가 3번 ({[l for l in hl if '맞았다' in l]})")

if 222 in learnable:
    a = learner([222]); d = neutral_target(222, [a])
    host, guest, hnoop, gnoop = battle(a, [222], d, [])
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    m = re.search(r"매그니튜드 (\d+)!", " ".join(hl))
    check(m and 4 <= int(m.group(1)) <= 10, f"매그니튜드 크기 표시 ({m.group(1) if m else None})")

if 515 in learnable:
    a = learner([515]); d = neutral_target(515, [a])
    host, guest, hnoop, gnoop = battle(a, [515], d, [])
    hp0 = host.me()["CurrentHp"]; before = host.opp()["CurrentHp"]
    (hl, hp), _ = play(host, guest, "USE_MOVE 1", gnoop)
    check(before - host.opp()["CurrentHp"] == min(hp0, before) and host.state["Team"][0]["Pokemon"]["CurrentHp"] == 0,
          "목숨걸기: 자신의 HP만큼 데미지를 주고 쓰러짐")

if 283 in learnable:
    a = learner([283]); d = neutral_target(283, [a])
    host, guest, hnoop, gnoop = battle(a, [283], d, [])
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    check(host.opp()["CurrentHp"] == host.me()["CurrentHp"] or own_fail(hl), "죽기살기: 상대 HP를 자신의 HP와 같게")

# 따라가때리기: 교체하려는 상대를 교체 전에 2배로
a = learner([228]); d = neutral_target(228, [a])
vals = []
for _ in range(5):
    host, guest, hnoop, gnoop = battle(a, [228], d, [])
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop); d1 = damage_of(hl, 228)
    (hl, _), (gl, _) = play(host, guest, "USE_MOVE 1", "SWITCH 2"); d2 = damage_of(hl, 228)
    order_ok = any(name(228) in l for l in gl) and [i for i, l in enumerate(gl) if name(228) in l][0] < [i for i, l in enumerate(gl) if "돌아와" in l][0]
    if d1 and d2 and order_ok: vals.append(d2 / d1)
mean = sum(vals) / max(1, len(vals))
check(len(vals) >= 2 and 1.5 <= mean <= 2.5, f"따라가때리기: 교체 전에 맞히고 약 2배 ({[round(v, 2) for v in vals]})")

# 내던지기: 지닌 도구가 없는 게임이라 위력 45로 고정. 같은 조건의 물기(위력 60)와 데미지 비율이 약 45/60
a = learner([374, 44]); d = neutral_target(374, [a])
flings, bites = [], []
for _ in range(6):
    host, guest, hnoop, gnoop = battle(a, [374, 44], d, [])
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop); flings.append(damage_of(hl, 374))
    (hl, _), _ = play(host, guest, "USE_MOVE 2", gnoop); bites.append(damage_of(hl, 44))
fl = [x for x in flings if x]; bt = [x for x in bites if x]
ratio = (sum(fl) / len(fl)) / (sum(bt) / len(bt)) if fl and bt else 0
check(len(fl) >= 3 and len(bt) >= 3 and 0.6 <= ratio <= 0.9, f"내던지기: 위력 45로 공격 ({flings} / 물기 {bites}, 비율 {ratio:.2f})")

# 8. 무작위 대전 (이 묶음 기술 포함)
p("\n[8] 무작위 대전")
import random
pool = [pid for (pid,) in con.execute("SELECT pokemon_id FROM Pokemon WHERE is_selectable=1")]
g5 = set(GROUP5)
ended = 0
for game in range(8):
    picks = []
    while len(picks) < 6:
        x = random.choice(pool)
        if SPECIES[x] not in {SPECIES[y] for y in picks} and len(learnset(x)) >= 5: picks.append(x)
    def moves_of(x):
        ls = learnset(x); first = [m for m in ls if m in g5][:2]
        return first + random.sample([m for m in ls if m not in first], 4 - len(first))
    team_of = lambda pids: [{"PokemonId": x, "MoveIds": moves_of(x)} for x in pids]
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
check(ended >= 6, f"무작위 대전 8판 중 {ended}판이 끝까지 진행")

server.kill()
p("\n총 실패:", len(failures))
sys.exit(1 if failures else 0)
