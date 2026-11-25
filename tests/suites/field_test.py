# 상태이상 마무리 테스트. 사용: python -I field_test.py <Server.exe> <Pokemon.db> <결과 파일> <서버 코드 폴더>
import glob, io, json, os, re, socket, sqlite3, subprocess, sys, time

exe, db, out_path, code_dir = sys.argv[1:5]
out = io.open(out_path, "w", encoding="utf-8")
port = 5080
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




# ---- 날씨·필드 ----
learnable = {m for (m,) in con.execute("SELECT DISTINCT move_id FROM PokemonLearnset")}
TYPE_OF_MOVE = dict(con.execute("SELECT move_id, type_id FROM PokemonMove"))
# 특별한 동작이 있는 기술(2턴·반동·자폭 등, 숨은 상대에게 맞는 기술)은 뺀다.
SPECIAL = {89, 57, 16, 239, 523, 13, 19, 76, 91, 130, 143, 291, 340, 467, 507, 553, 554, 566,
           63, 307, 308, 338, 416, 439, 459, 120, 153, 26, 136, 389, 264, 387, 248, 353, 311, 362}
plain_of = lambda type_name: {m for m in plain if TYPE_OF_MOVE[m] == TYPES[type_name] and m not in SPECIAL}

def damage_of(lines, move_id):
    for l in lines:
        m = re.search(r"\((\d+) 데미지\)", l)
        if m and name(move_id) + "!" in l and "급소" not in l and "번 맞았다" not in l: return int(m.group(1))
    return None

def heal_of(lines):
    m = re.search(r"체력을 회복했다! \(\+(\d+)\)", " ".join(lines))
    return int(m.group(1)) if m else None

# 1. 날씨 시작·지속·끝, 같은 날씨는 실패
p("\n[1] 날씨 5턴")
a = learner([201]); d = learner(exclude=[a], order="base_hp DESC", without_types=["바위", "땅", "강철"])
host, guest, hnoop, gnoop = battle(a, [201], d, [])
(hl, _), (gl, _) = play(host, guest, "USE_MOVE 1", gnoop)
mx = host.opp()["MaxHp"]
check(has(hl, "모래바람이 불기 시작했다") and has(gl, "모래바람에 의한 데미지") and host.opp()["CurrentHp"] == mx - mx // 16,
      f"모래바람 시작, 턴 끝 1/16 데미지 ({host.opp()['CurrentHp']}/{mx})")
check(host.state["FieldEffects"] == ["날씨: 모래바람 (4턴)"] and guest.state["FieldEffects"] == host.state["FieldEffects"],
      f"화면 정보에 날씨와 남은 턴 ({host.state['FieldEffects']})")
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(own_fail(hl), "같은 날씨는 실패")
ends = []
for t in range(3, 7):
    (hl, _), _ = play(host, guest, hnoop, gnoop)
    ends.append(has(hl, "모래바람이 가라앉았다"))
check(ends == [False, False, True, False], f"쓴 턴부터 5번째 턴 끝에 끝남 ({ends})")
check(host.state["FieldEffects"] == [], f"날씨가 끝나면 화면 정보에서 빠짐 ({host.state['FieldEffects']})")

try:
    tr = learner([240, 433])
    host, guest, hnoop, gnoop = battle(tr, [240, 433], learner(exclude=[tr], order="base_hp DESC"), [])
    play(host, guest, "USE_MOVE 1", gnoop)
    play(host, guest, "USE_MOVE 2", gnoop)
    check(host.state["FieldEffects"] == ["날씨: 비 (3턴)", "트릭룸 (4턴)"], f"날씨와 트릭룸을 함께 표시 ({host.state['FieldEffects']})")
except RuntimeError:
    p("   비바라기 + 트릭룸 포켓몬 없음")
d = learner(with_type="바위", exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [201], d, [])
(hl, _), (gl, _) = play(host, guest, "USE_MOVE 1", gnoop)
check(not has(gl, "모래바람에 의한") and host.opp()["CurrentHp"] == host.opp()["MaxHp"], "바위 타입은 모래바람 데미지 없음")

a = learner([258], without_types=["얼음"]); d = learner(exclude=[a], order="base_hp DESC", without_types=["얼음"])
host, guest, hnoop, gnoop = battle(a, [258], d, [])
(hl, _), (gl, _) = play(host, guest, "USE_MOVE 1", gnoop)
check(has(gl, "싸라기눈을 맞았다"), "싸라기눈 턴 끝 데미지")

# 2. 비·쾌청에서 물 기술 1.5배·0.5배
def weather_ratio(label, weather_move, attack_type, expected):
    # 공격하는 쪽은 아무 효과 없는 기술이 필요 없으므로 팀을 직접 만든다.
    a = learner([weather_move], need_noop=False, extra=lambda ls: any(m in plain_of(attack_type) for m in ls))
    hit = next(m for m in learnset(a) if m in plain_of(attack_type))
    d = learner(exclude=[a], order="base_hp DESC", without_types=["물", "풀", "드래곤", "불꽃", "바위", "땅"])
    rest = [m for m in learnset(a) if m not in (hit, weather_move)][:2]
    ratios = []
    for _ in range(4):
        gt = [member(d, [])] + fillers([a, d])
        host, guest = start([{"PokemonId": a, "MoveIds": [hit, weather_move] + rest}] + fillers([a, d]), gt)
        gnoop = f"USE_MOVE {noop_slot(gt[0])}"
        (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop); d1 = damage_of(hl, hit)
        play(host, guest, "USE_MOVE 2", gnoop)
        (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop); d2 = damage_of(hl, hit)
        if d1 and d2: ratios.append(d2 / d1)
    mean = sum(ratios) / max(1, len(ratios))
    p(f"   {name(hit)} 비율 {[round(r, 2) for r in ratios]}")
    check(len(ratios) >= 2 and abs(mean - expected) <= expected * 0.2, f"{label}: 평균 {mean:.2f} (기대 {expected})")

p("\n[2] 날씨 데미지 보정")
done = 0
for label, wmove, atype, expected in (("비 + 물", 240, "물", 1.5), ("비 + 불꽃", 240, "불꽃", 0.5),
                                      ("쾌청 + 불꽃", 241, "불꽃", 1.5), ("쾌청 + 물", 241, "물", 0.5)):
    try:
        weather_ratio(label, wmove, atype, expected); done += 1
    except RuntimeError:
        p(f"   {label}: 배우는 포켓몬 없음")
check(done >= 3, f"날씨 보정 {done}가지 확인")

# 3. 비에서 번개는 반드시 맞음, 쾌청에서 솔라빔은 바로 발사
p("\n[3] 날씨와 기술")
a = learner([240, 87]); d = learner(exclude=[a], order="base_hp DESC", without_types=["땅"])
host, guest, hnoop, gnoop = battle(a, [240, 87], d, [])
play(host, guest, "USE_MOVE 1", gnoop)
misses = 0
for _ in range(4):
    (hl, _), _ = play(host, guest, "USE_MOVE 2", gnoop)
    misses += has(hl, "빗나갔다")
    if host.opp()["CurrentHp"] == 0: break
check(misses == 0, f"비에서 번개 빗나감 0번 ({misses})")

a = learner([241, 76]); d = learner(exclude=[a], order="base_hp DESC", without_types=["불꽃", "풀", "드래곤", "벌레", "독", "비행", "강철"])
host, guest, hnoop, gnoop = battle(a, [241, 76], d, [])
play(host, guest, "USE_MOVE 1", gnoop)
(hl, hp), _ = play(host, guest, "USE_MOVE 2", gnoop)
check(not has(hl, "빛을 흡수했다") and host.opp()["CurrentHp"] < host.opp()["MaxHp"] and hp == "CHOOSE_ACTION", "쾌청에서 솔라빔 바로 발사")

wb = [(240, "불꽃"), (241, "풀")]
for wmove, weak in wb:
    try:
        a = learner([311, wmove]); d = learner(with_type=weak, exclude=[a], order="base_hp DESC", without_types=["물", "드래곤", "불꽃", "바위"])
    except RuntimeError:
        continue
    host, guest, hnoop, gnoop = battle(a, [311, wmove], d, [])
    play(host, guest, "USE_MOVE 2", gnoop)
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    p("  ", hl)
    check(any(name(311) in l and "효과가 굉장했다" in l for l in hl), f"{name(wmove)} 중 웨더볼이 {weak} 타입에게 효과가 굉장함")
    break

heal = next(m for m in (235, 234, 236) if m in learnable)
a = learner([241, heal], order="base_hp DESC")
d = learner(exclude=[a], order="base_attack DESC", extra=lambda ls: any(m in plain for m in ls), without_types=["고스트"])
dhit = next(m for m in learnset(d) if m in plain)
ok = tried = 0
for _ in range(3):
    host, guest, hnoop, gnoop = battle(a, [241, heal], d, [dhit])
    play(host, guest, "USE_MOVE 1", "USE_MOVE 1")
    me = host.me()
    if me["CurrentHp"] in (0, me["MaxHp"]): continue
    missing = me["MaxHp"] - me["CurrentHp"]
    (hl, _), _ = play(host, guest, "USE_MOVE 2", gnoop)
    tried += 1; ok += heal_of(hl) == min(me["MaxHp"] * 2 // 3, missing)
check(tried > 0 and ok == tried, f"쾌청에서 {name(heal)} 2/3 회복 ({ok}/{tried})")

if 74 in learnable:
    a = learner([241, 74]); d = learner(exclude=[a], order="base_hp DESC")
    host, guest, hnoop, gnoop = battle(a, [241, 74], d, [])
    play(host, guest, "USE_MOVE 1", gnoop)
    play(host, guest, "USE_MOVE 2", gnoop)
    check(sorted(changes(host.me())) == ["공격 +2", "특수공격 +2"], f"쾌청에서 성장 +2 ({changes(host.me())})")

# 4. 필드
p("\n[4] 필드")
a = learner([604], without_types=["비행", "풀"]); d = learner([147], exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [604], d, [147])
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(has(hl, "발밑에 전기가 흐르기 시작했다"), "일렉트릭필드 시작")
check(host.state["FieldEffects"] == ["필드: 일렉트릭필드 (4턴)"], f"화면 정보에 필드와 남은 턴 ({host.state['FieldEffects']})")
(hl, _), _ = play(host, guest, hnoop, "USE_MOVE 1")
p("  ", hl)
check(has(hl, "일렉트릭필드에 보호받고 있다") and host.me()["Status"] is None, "일렉트릭필드에서 잠들지 않음")

a = learner([581], without_types=["비행", "독", "강철"]); d = learner([92], exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [581], d, [92])
play(host, guest, "USE_MOVE 1", gnoop)
(hl, _), _ = retry_hl = play(host, guest, hnoop, "USE_MOVE 1")
for _ in range(5):
    if not has(hl, "빗나갔다"): break
    (hl, _), _ = play(host, guest, hnoop, "USE_MOVE 1")
check(has(hl, "미스트필드에 보호받고 있다") and host.me()["Status"] is None, "미스트필드에서 맹독에 걸리지 않음")

a = learner([580], without_types=["비행"], order="base_hp DESC")
d = learner(exclude=[a], order="base_attack DESC", extra=lambda ls: any(m in plain for m in ls), without_types=["고스트"])
dhit = next(m for m in learnset(d) if m in plain)
host, guest, hnoop, gnoop = battle(a, [580], d, [dhit])
(hl, _), _ = play(host, guest, "USE_MOVE 1", "USE_MOVE 1")
p("  ", hl)
check(has(hl, "발밑에 풀이 무성해졌다") and (heal_of(hl) == host.me()["MaxHp"] // 16 or host.me()["CurrentHp"] == host.me()["MaxHp"]),
      "그래스필드 턴 끝 1/16 회복")

# 5. 중력·원더룸·흙놀이·페어리록·플라스마샤워
p("\n[5] 중력 등")
a = learner([356]); d = learner([19], exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [356], d, [19])
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(has(hl, "중력이 강해졌다") and host.state["FieldEffects"] == ["중력 (4턴)"], f"중력 시작, 화면 정보 ({host.state['FieldEffects']})")
(hl, gp), (gl, _) = play(host, guest, hnoop, "USE_MOVE 1")
check(has(gl, "중력 때문에") and not has(gl, "날아올랐다"), "중력 중에는 공중날기 실패")
try:
    a = learner([356, 89]); eq = 89
    d = learner(with_type="비행", exclude=[a], order="base_hp DESC")
    host, guest, hnoop, gnoop = battle(a, [356, eq], d, [])
    (hl, _), _ = play(host, guest, "USE_MOVE 2", gnoop)
    check(has(hl, "효과가 없는"), f"중력 전에는 {name(eq)}가 비행 타입에게 효과 없음")
    play(host, guest, "USE_MOVE 1", gnoop)
    (hl, _), _ = play(host, guest, "USE_MOVE 2", gnoop)
    check(any(name(eq) in l and "데미지)" in l for l in hl), "중력 중에는 비행 타입에게 땅 기술이 맞음")
except RuntimeError:
    p("   땅 기술 + 중력 포켓몬 없음")

a = learner([472]); d = learner(exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [472], d, [])
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
(hl2, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(has(hl, "이상한 공간이 펼쳐졌다") and has(hl2, "원더룸이 원래대로"), "원더룸 시작, 다시 쓰면 끝남")

for sport, text in ((300, "전기의 위력이 약해졌다"), (346, "불꽃의 위력이 약해졌다")):
    a = learner([sport]); d = learner(exclude=[a], order="base_hp DESC")
    host, guest, hnoop, gnoop = battle(a, [sport], d, [])
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    (hl2, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    check(has(hl, text) and has(hl2, "하지만 실패했다"), f"{name(sport)} 시작, 중복 실패")

if 587 in learnable:
    a = learner([587]); d = learner(exclude=[a], order="base_hp DESC")
    host, guest, hnoop, gnoop = battle(a, [587], d, [])
    play(host, guest, "USE_MOVE 1", gnoop)
    guest.send("SWITCH 2")
    err = guest.read_until("ERROR")[-1]
    check("페어리록" in err, f"페어리록 다음 턴 교체 불가 ({err})")
    play(host, guest, hnoop, gnoop)
    (hl, _), (gl, _) = play(host, guest, hnoop, "SWITCH 2")
    check(has(gl, "가라"), "그다음 턴에는 교체 가능")

if 569 in learnable:
    # 플라스마샤워(우선도 +1)를 쓴 턴에 상대의 노말 기술 연출(BATTLE_EFFECT)이 전기 타입으로 나오는지 본다.
    a = learner([569]); d = learner(exclude=[a], extra=lambda ls: any(m in plain_of("노말") for m in ls), without_types=["고스트"])
    nhit = next(m for m in learnset(d) if m in plain_of("노말"))
    host, guest, hnoop, gnoop = battle(a, [569], d, [nhit])
    host.send("USE_MOVE 1"); guest.send("USE_MOVE 1")
    lines = guest.read_until(PROMPTS); host.turn()
    effects = [l.split()[1] for l in lines if l.startswith("BATTLE_EFFECT ")]
    p("  ", [l[11:] for l in lines if l.startswith("BATTLE_LOG ")], effects)
    check(any("전자가 흩뿌려졌다" in l for l in lines) and str(TYPES["전기"]) in effects,
          f"플라스마샤워 턴에 {name(nhit)}이 전기 타입")

# 6. 무작위 대전
p("\n[6] 무작위 대전")
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
