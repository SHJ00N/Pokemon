# 상태이상 마무리 테스트. 사용: python -I effect_test.py <Server.exe> <Pokemon.db> <결과 파일> <서버 코드 폴더>
import glob, io, json, os, re, socket, sqlite3, subprocess, sys, time

exe, db, out_path, code_dir = sys.argv[1:5]
out = io.open(out_path, "w", encoding="utf-8")
port = 5083
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




# ---- 6번 묶음: 나머지 변화기·공격기 ----
learnable = {m for (m,) in con.execute(
    "SELECT DISTINCT l.move_id FROM PokemonLearnset l JOIN Pokemon p USING(pokemon_id) WHERE p.is_selectable=1")}
CATEGORY = dict(con.execute("SELECT move_id, category FROM PokemonMove"))
MTYPE = dict(con.execute("SELECT move_id, type_id FROM PokemonMove"))
FLAG = {}
for m, f in con.execute("SELECT move_id, flag FROM MoveFlag"): FLAG.setdefault(m, set()).add(f)
types_of = lambda view: sorted(t["Name"] for t in view["Types"])
moves_of_view = lambda pl: [mv["MoveId"] for mv in pl.state["Team"][pl.state["Active"]]["Moves"]]
GROUP6 = [50, 227, 259, 269, 286, 377, 180, 73, 171, 174, 195, 194, 288, 170, 199, 193, 316, 357, 477, 160, 176, 293,
          487, 513, 567, 571, 102, 118, 119, 166, 267, 274, 382, 383, 289, 144, 588, 596, 561, 578, 469, 150, 287, 379,
          576, 393, 505, 600, 601, 599, 563, 579, 582, 280, 364, 593, 621, 479, 253, 99, 206, 565]
# 능력치만 바꾸고 다른 일은 일으키지 않는 변화 기술 (발버둥 확인용)
SAFE_STATUS = {m for (m,) in con.execute("SELECT DISTINCT move_id FROM MoveStatChange") if CATEGORY[m] == "변화"} - {
    262, 575, 187, 432, 597, 602, 563, 579, 599, 601, 174, 254, 268, 367}
contact_plain = lambda ls: [m for m in ls if m in plain and "contact" in FLAG.get(m, set())]

def send_expect_error(pl, command):
    pl.send(command)
    return pl.read_until("ERROR")[-1]

# 1. 모든 기술이 "아직 구현되지 않은 기술"로 나오지 않는다
p("\n[1] 미구현 표시 없음")
bad, tried, unusable = [], 0, []
for m in GROUP6:
    if m not in learnable: continue
    try:
        a = learner([m])
    except RuntimeError:   # 배우는 포켓몬이 기술을 4개 고를 수 없음 (스케치만 배우는 루브도 등)
        unusable.append(name(m)); continue
    d = learner(exclude=[a], order="base_hp DESC")
    host, guest, hnoop, gnoop = battle(a, [m], d, [])
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    tried += 1
    if any(name(m) in l and "아직 구현되지 않은" in l for l in hl): bad.append(name(m))
p("   배울 수 있는 포켓몬이 없는 기술:", [name(m) for m in GROUP6 if m not in learnable] or "없음")
p("   팀에 넣을 수 없는 기술:", unusable or "없음")
check(not bad, f"{tried}개 사용, 미구현 표시 {bad}")

# 2. 기술 제한
p("\n[2] 기술 제한")
a = learner([269]); d = learner(exclude=[a], order="base_hp DESC", extra=lambda ls: any(m in plain for m in ls))
dhit = next(m for m in learnset(d) if m in plain)
host, guest, hnoop, gnoop = battle(a, [269], d, [dhit])
play(host, guest, "USE_MOVE 1", "USE_MOVE 1")
err = send_expect_error(guest, gnoop)
check("도발" in err, f"도발 중에는 변화 기술 선택 불가 ({err})")
guest.send("USE_MOVE 1"); host.send(hnoop); host.turn(); guest.turn()

# 사슬묶기·앙코르: 상대가 두 턴 모두 noop을 쓰게 해서 순서와 상관없이 마지막 기술이 noop이 되게 한다.
a = learner([50]); d = learner(exclude=[a], order="base_hp DESC", extra=lambda ls: any(m in plain for m in ls))
dhit = next(m for m in learnset(d) if m in plain)
host, guest, hnoop, gnoop = battle(a, [50], d, [dhit])
play(host, guest, hnoop, gnoop)
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(has(hl, "쓸 수 없게 되었다"), "사슬묶기 사용")
err = send_expect_error(guest, gnoop)
check("사슬묶기" in err, f"사슬묶기된 기술 선택 불가 ({err})")
guest.send("USE_MOVE 1"); host.send(hnoop); host.turn(); guest.turn()

a = learner([227]); d = learner(exclude=[a], order="base_hp DESC", extra=lambda ls: any(m in plain for m in ls))
dhit = next(m for m in learnset(d) if m in plain)
host, guest, hnoop, gnoop = battle(a, [227], d, [dhit])
play(host, guest, hnoop, gnoop)
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(has(hl, "앙코르를 받았다"), "앙코르")
err = send_expect_error(guest, "USE_MOVE 1")
check("앙코르" in err, f"앙코르 중에는 다른 기술 선택 불가 ({err})")
guest.send(gnoop); host.send(hnoop); host.turn(); guest.turn()

# 발버둥: 도발 중인데 변화 기술만 4개면 어떤 기술을 골라도 발버둥
d = learner(order="base_hp DESC", need_noop=False, extra=lambda ls: sum(1 for m in ls if m in SAFE_STATUS) >= 4)
a = learner([269], exclude=[d])
gmoves = [m for m in learnset(d) if m in SAFE_STATUS][:4]
ht = [member(a, [269])] + fillers([a, d]); gt = [{"PokemonId": d, "MoveIds": gmoves}] + fillers([a, d])
host, guest = start(ht, gt)
hnoop = f"USE_MOVE {noop_slot(ht[0])}"
play(host, guest, "USE_MOVE 1", "USE_MOVE 1")
(hl, _), (gl, _) = play(host, guest, hnoop, "USE_MOVE 2")
check(has(gl, "의 발버둥!") and has(gl, "반동으로 데미지를 입었다"), f"쓸 수 있는 기술이 없으면 발버둥 (반동 1/4) {gl}")

# 3. 턴마다 효과
p("\n[3] 턴마다 효과")
a = learner([73], order="base_hp DESC"); d = learner(exclude=[a], order="base_hp DESC", without_types=["풀"])
host, guest, hnoop, gnoop = battle(a, [73], d, [])
for _ in range(5):
    (hl, _), (gl, _) = play(host, guest, "USE_MOVE 1", gnoop)
    if has(gl, "씨를 심었다"): break
mx = host.opp()["MaxHp"]
check(has(gl, "체력을 빼앗는다") and host.opp()["CurrentHp"] == mx - mx // 8, f"씨뿌리기: 턴 끝 1/8 ({host.opp()['CurrentHp']}/{mx})")

ghost_curse = learner([174], with_type="고스트"); d = learner(exclude=[ghost_curse], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(ghost_curse, [174], d, [])
mx_me, mx = host.me()["MaxHp"], host.opp()["MaxHp"]
play(host, guest, "USE_MOVE 1", gnoop)
check(host.me()["CurrentHp"] == mx_me - mx_me // 2 and host.opp()["CurrentHp"] == mx - mx // 4, "고스트의 저주: 자신 1/2, 상대는 턴 끝 1/4")
normal_curse = learner([174], without_types=["고스트"]); d = learner(exclude=[normal_curse], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(normal_curse, [174], d, [])
play(host, guest, "USE_MOVE 1", gnoop)
check(sorted(changes(host.me())) == ["공격 +1", "방어 +1", "스피드 -1"], f"고스트가 아니면 능력치 변화 ({changes(host.me())})")

a = learner([195]); d = learner(exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [195], d, [])
counts = []
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop); counts += re.findall(r"멸망 카운트가 (\d)", " ".join(hl))
for _ in range(3):
    (hl, hp), _ = play(host, guest, hnoop, gnoop); counts += re.findall(r"멸망 카운트가 (\d)", " ".join(hl))
check(host.state["Team"][0]["Pokemon"]["CurrentHp"] == 0 and host.opp()["CurrentHp"] == 0 and "0" in counts,
      f"멸망의노래: 3턴 뒤 양쪽이 쓰러짐 ({counts})")

# 4. 명중·타입
p("\n[4] 명중·타입")
pair = None
for lock in (199, 170):
    for ohko in (32, 12, 329, 90):
        try:
            pair = (learner([lock, ohko]), lock, ohko); break
        except RuntimeError:
            pass
    if pair: break
a, lock, ohko = pair
d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트", "비행"])
host, guest, hnoop, gnoop = battle(a, [lock, ohko], d, [])
play(host, guest, "USE_MOVE 1", gnoop)
(hl, _), _ = play(host, guest, "USE_MOVE 2", gnoop)
check(has(hl, "일격필살") and host.opp()["CurrentHp"] == 0, f"{name(lock)} 다음 턴 {name(ohko)}이(가) 반드시 맞음")

a = learner([193], extra=lambda ls: any(m in plain and MTYPE[m] == TYPES["노말"] for m in ls))
nhit = next(m for m in learnset(a) if m in plain and MTYPE[m] == TYPES["노말"])
d = learner(with_type="고스트", exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [193, nhit], d, [])
(hl, _), _ = play(host, guest, "USE_MOVE 2", gnoop)
immune = any(name(nhit) in l and "효과가 없는" in l for l in hl)
play(host, guest, "USE_MOVE 1", gnoop)
(hl, _), _ = play(host, guest, "USE_MOVE 2", gnoop)
check(immune and any(name(nhit) in l and "데미지)" in l for l in hl), "꿰뚫어보기 후 노말 기술이 고스트에게 맞음")

a = learner([393]); d = learner([89], exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [393], d, [89])
play(host, guest, "USE_MOVE 1", gnoop)
(hl, _), _ = play(host, guest, hnoop, "USE_MOVE 1")
check(host.me()["CurrentHp"] == host.me()["MaxHp"], "전자부유 중에는 지진을 맞지 않음")

a = learner([487]); d = learner(exclude=[a], order="base_hp DESC", without_types=["물"])
host, guest, hnoop, gnoop = battle(a, [487], d, [])
play(host, guest, "USE_MOVE 1", gnoop)
check(types_of(host.opp()) == ["물"], f"물붓기: 상대가 물 타입 ({types_of(host.opp())})")
a = learner([567]); d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트"])
host, guest, hnoop, gnoop = battle(a, [567], d, [])
before = types_of(host.opp())
play(host, guest, "USE_MOVE 1", gnoop)
check(types_of(host.opp()) == sorted(before + ["고스트"]), f"핼러윈: 고스트 타입 추가 ({types_of(host.opp())})")

a = learner([144]); d = learner(exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [144], d, [])
play(host, guest, "USE_MOVE 1", gnoop)
check(moves_of_view(host) == moves_of_view(guest) and types_of(host.me()) == types_of(host.opp()),
      "변신: 상대의 타입·기술을 따라 함")

a = learner([102]); d = learner(exclude=[a], order="base_hp DESC", extra=lambda ls: any(m in plain for m in ls))
dhit = next(m for m in learnset(d) if m in plain)
host, guest, hnoop, gnoop = battle(a, [102], d, [dhit])
play(host, guest, hnoop, "USE_MOVE 1")
play(host, guest, "USE_MOVE 1", "USE_MOVE 1")
check(moves_of_view(host)[0] == dhit, f"흉내내기: 상대 기술을 배움 ({name(moves_of_view(host)[0])})")

# 5. 다른 기술을 쓰는 기술·가로채기
p("\n[5] 다른 기술 쓰기")
a = learner([118]); d = learner(exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [118], d, [])
me_name = host.me()["Name"]
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
i = next(i for i, l in enumerate(hl) if l == f"{me_name}의 {name(118)}!")
check(i + 1 < len(hl) and hl[i + 1].startswith(me_name) and name(118) not in hl[i + 1],
      f"손가락흔들기: 다른 기술이 나옴 ({hl[i + 1] if i + 1 < len(hl) else None})")
a = learner([289], order="base_speed ASC"); d = learner([14], exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [289], d, [14])
(hl, _), _ = play(host, guest, "USE_MOVE 1", "USE_MOVE 1")
check(has(hl, "가로챘다") and changes(host.me()) == ["공격 +2"] and changes(host.opp()) == [],
      f"가로채기: 상대의 칼춤을 가로챔 ({changes(host.me())} / {changes(host.opp())})")

# 6. 방어 변형
p("\n[6] 방어 변형")
for shield in (588, 596):
    a = learner([shield], order="base_hp DESC")
    d = learner(exclude=[a], order="base_hp DESC", without_types=["고스트"], extra=lambda ls: bool(contact_plain(ls)))
    dhit = contact_plain(learnset(d))[0]
    host, guest, hnoop, gnoop = battle(a, [shield], d, [dhit])
    (hl, _), _ = play(host, guest, "USE_MOVE 1", "USE_MOVE 1")
    if shield == 588:
        check(has(hl, "몸을 지켰다") and "공격 -2" in changes(host.opp()), f"킹실드: 막고 접촉한 상대 공격 -2 ({changes(host.opp())})")
    else:
        mx = host.opp()["MaxHp"]
        check(has(hl, "니들가드에 찔렸다") and host.opp()["CurrentHp"] == mx - mx // 8, "니들가드: 접촉한 상대 1/8 데미지")
a = learner([364]); d = learner([182], exclude=[a], order="base_hp DESC", without_types=["고스트"])
host, guest, hnoop, gnoop = battle(a, [364], d, [182])
(hl, _), _ = play(host, guest, "USE_MOVE 1", "USE_MOVE 1")
check(has(hl, "방어가 풀렸다") and host.opp()["CurrentHp"] < host.opp()["MaxHp"], "페인트가 방어를 뚫음")
a = learner([469], without_types=["비행"]); d = learner([89], exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [469], d, [89])
(hl, _), _ = play(host, guest, "USE_MOVE 1", "USE_MOVE 1")
check(has(hl, "몸을 지켰다") and host.me()["CurrentHp"] == host.me()["MaxHp"], "와이드가드가 지진을 막음")

# 7. 공격기
p("\n[7] 공격기")
a = learner([280]); d = learner([115], exclude=[a], order="base_hp DESC", without_types=["고스트"])
host, guest, hnoop, gnoop = battle(a, [280], d, [115])
play(host, guest, hnoop, "USE_MOVE 1")
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(has(hl, "리플렉터·빛의장막이 깨졌다"), "깨뜨리다가 리플렉터를 없앰")
a = learner([479, 89]); d = learner(with_type="비행", exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [479, 89], d, [])
(hl, _), _ = play(host, guest, "USE_MOVE 2", gnoop)
immune = any(name(89) in l and "효과가 없는" in l for l in hl)
play(host, guest, "USE_MOVE 1", gnoop)
(hl, _), _ = play(host, guest, "USE_MOVE 2", gnoop)
check(immune and any(name(89) in l and "데미지)" in l for l in hl), "떨어뜨리기 후 비행 타입에게 지진이 맞음")
a = learner([206]); d = learner(exclude=[a], order="base_hp ASC", without_types=["고스트"])
host, guest, hnoop, gnoop = battle(a, [206], d, [])
for _ in range(10):
    play(host, guest, "USE_MOVE 1", gnoop)
check(host.opp()["CurrentHp"] == 1, f"칼등치기는 HP 1을 남김 ({host.opp()['CurrentHp']})")
a = learner([253], without_types=["풀", "고스트"]); d = learner([147], exclude=[a], order="base_hp DESC")
host, guest, hnoop, gnoop = battle(a, [253], d, [147])
(hl, hp), _ = play(host, guest, "USE_MOVE 1", gnoop)
play(host, guest, None, "USE_MOVE 1")
check(hp == "LOCKED" and host.me()["Status"] is None, f"소란피기: 이어서 사용(LOCKED), 그동안 잠들지 않음 ({hp}, {host.me()['Status']})")

# 8. 무작위 대전
p("\n[8] 무작위 대전")
import random
pool = [pid for (pid,) in con.execute("SELECT pokemon_id FROM Pokemon WHERE is_selectable=1")]
g6 = set(GROUP6)
ended = 0
for game in range(8):
    picks = []
    while len(picks) < 6:
        x = random.choice(pool)
        if SPECIES[x] not in {SPECIES[y] for y in picks} and len(learnset(x)) >= 5: picks.append(x)
    def moves_of(x):
        ls = learnset(x); first = random.sample([m for m in ls if m in g6], min(2, sum(1 for m in ls if m in g6)))
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
