# 상태이상 마무리 테스트. 사용: python -I status_test.py <Server.exe> <Pokemon.db> <결과 파일> <서버 코드 폴더>
import glob, io, json, os, re, socket, sqlite3, subprocess, sys, time

exe, db, out_path, code_dir = sys.argv[1:5]
out = io.open(out_path, "w", encoding="utf-8")
port = 5076
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



# ---- 상태이상 마무리 ----
learnable = {m for (m,) in con.execute("SELECT DISTINCT move_id FROM PokemonLearnset")}
TYPE_OF = {pid: {t1, t2} for pid, t1, t2 in con.execute("SELECT pokemon_id, type1_id, type2_id FROM Pokemon")}
status_of = lambda pl, i: pl.state["Team"][i]["Pokemon"]["Status"]

def retry(host, guest, h, g, ok, tries=8):
    for _ in range(tries):
        (hl, _), (gl, _) = play(host, guest, h, g)
        if ok(): return hl, gl
    return hl, gl

def damage_of(lines, move_id):
    for l in lines:
        m = re.search(r"\((\d+) 데미지\)", l)
        if m and name(move_id) + "!" in l and "급소" not in l: return int(m.group(1))
    return None

# 1. 치료방울·아로마테라피: 교체로 들어가 있는 포켓몬까지 팀 전체를 고친다
for cure in (215, 312):
    p(f"\n[1] {name(cure)}")
    if cure not in learnable: p("   배우는 포켓몬 없음"); continue
    d = learner([92], with_type="독")
    a = learner([cure], without_types=["독", "강철"], exclude=[d])
    b = learner(without_types=["독", "강철"], exclude=[a, d], order="base_hp DESC")
    c = learner(without_types=["독", "강철"], exclude=[a, b, d], order="base_hp DESC")
    ht = [member(a, [cure]), member(b, []), member(c, [])]; gt = [member(d, [92])] + fillers([a, b, c, d])
    host, guest = start(ht, gt)
    hnoop, gnoop = f"USE_MOVE {noop_slot(ht[0])}", f"USE_MOVE {noop_slot(gt[0])}"
    play(host, guest, hnoop, "USE_MOVE 1")
    play(host, guest, "SWITCH 2", "USE_MOVE 1")
    play(host, guest, "SWITCH 1", gnoop)
    check(status_of(host, 0) == "맹독" and status_of(host, 1) == "맹독", f"앞의 두 마리 맹독 ({status_of(host, 0)}, {status_of(host, 1)})")
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    p("  ", hl)
    check(status_of(host, 0) is None and status_of(host, 1) is None and sum("상태가 나았다" in l for l in hl) == 2,
          "출전 중인 포켓몬과 교체로 들어가 있는 포켓몬 모두 나음")
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    check(own_fail(hl), "고칠 상태이상이 없으면 실패")

# 2. 사이코시프트: 내 상태이상을 상대에게 옮긴다
p("\n[2] 사이코시프트")
a = learner([375], without_types=["독", "강철"])
d = learner([92], exclude=[a], order="base_hp DESC", without_types=["독", "강철"])
host, guest, hnoop, gnoop = battle(a, [375], d, [92])
retry(host, guest, hnoop, "USE_MOVE 1", lambda: host.me()["Status"] is not None)
check(host.me()["Status"] == "맹독", "상대의 맹독으로 맹독 상태")
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
p("  ", hl)
check(host.me()["Status"] is None and host.opp()["Status"] == "맹독", f"맹독을 상대에게 옮김 (나 {host.me()['Status']}, 상대 {host.opp()['Status']})")
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(own_fail(hl), "내 상태이상이 없으면 실패")
d = learner([92], with_type="독", exclude=[a])
host, guest, hnoop, gnoop = battle(a, [375], d, [92])
play(host, guest, hnoop, "USE_MOVE 1")
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
check(host.me()["Status"] == "맹독" and host.opp()["Status"] is None and own_fail(hl), "독 타입에게는 옮길 수 없음")

# 3. 잠꼬대: 깨어 있으면 실패, 잠들어 있으면 다른 기술을 무작위로 쓴다(2턴 기술 등은 안 나옴)
p("\n[3] 잠꼬대")
banned = [76, 91, 19, 291, 340]
plain_a = lambda ls: any(m in plain and m not in banned for m in ls) and any(m in ls for m in banned)
a = learner([214], without_types=["풀"], extra=plain_a)
hit = next(m for m in learnset(a) if m in plain and m not in banned); ban = next(m for m in banned if m in learnset(a))
d = learner([147], exclude=[a], order="base_hp DESC")
called = []
for game in range(3):
    ht = [{"PokemonId": a, "MoveIds": [214, hit, ban, next(m for m in learnset(a) if m in NO_EFFECT)]}] + fillers([a, d])
    gt = [member(d, [147])] + fillers([a, d])
    host, guest = start(ht, gt)
    gnoop = f"USE_MOVE {noop_slot(gt[0])}"
    if game == 0:
        (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
        check(own_fail(hl), "깨어 있을 때 잠꼬대 실패")
    play(host, guest, "USE_MOVE 4", "USE_MOVE 1")
    while host.me()["Status"] == "잠듦":
        (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
        if not has(hl, "쿨쿨"): break
        i = next(i for i, l in enumerate(hl) if name(214) + "!" in l)
        called.append(next(mid for mid in (214, hit, ban, ht[0]["MoveIds"][3]) if name(mid) + "!" in hl[i + 1]))
p(f"   나온 기술: {[name(m) for m in called]}")
check(len(called) >= 2 and all(m in (hit, ht[0]["MoveIds"][3]) for m in called),
      f"잠든 채로 다른 기술을 씀 ({name(ban)}·잠꼬대는 안 나옴)")

# 4~7. 위력 2배 기술: 상태이상 전/후 데미지 비율
def ratio_test(label, attack, setup, a_kw, d_kw, attacker_status=False):
    p(f"\n[{label}] {name(attack)}")
    req = [attack] + ([] if attacker_status else [setup])
    a = learner(req, **a_kw)
    d = learner([setup] if attacker_status else [], exclude=[a], order="base_hp DESC", **d_kw)
    ratios = []
    for _ in range(4):
        host, guest, hnoop, gnoop = battle(a, [attack] if attacker_status else [attack, setup], d,
                                           [setup] if attacker_status else [])
        (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
        before = damage_of(hl, attack)
        if attacker_status:
            retry(host, guest, hnoop, "USE_MOVE 1", lambda: host.me()["Status"] is not None)
            ok = host.me()["Status"] is not None
        else:
            retry(host, guest, "USE_MOVE 2", gnoop, lambda: host.opp()["Status"] is not None)
            ok = host.opp()["Status"] is not None
        if not ok or host.opp()["CurrentHp"] == 0: continue
        (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
        after = damage_of(hl, attack)
        if before and after: ratios.append(after / before)
    mean = sum(ratios) / max(1, len(ratios))
    p(f"   비율 {[round(r, 2) for r in ratios]}")
    check(len(ratios) >= 2 and 1.6 <= mean <= 2.5, f"상태이상이면 위력 2배 (평균 {mean:.2f})")

ratio_test("4", 263, 92, dict(without_types=["독", "강철"]), dict(with_type="독", without_types=["고스트", "바위", "강철"]), attacker_status=True)
ratio_test("5", 263, 261, dict(without_types=["불꽃"]), dict(without_types=["고스트", "바위", "강철", "불꽃"]), attacker_status=True)
ratio_test("6", 506, 92, dict(), dict(without_types=["노말", "독", "강철", "악"]))
ratio_test("7", 474, 92, dict(), dict(without_types=["독", "강철", "땅", "바위", "고스트"]))

server.kill()
p("\n총 실패:", len(failures))
sys.exit(1 if failures else 0)
