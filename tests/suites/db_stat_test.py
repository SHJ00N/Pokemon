# 6세대 규칙 확인 테스트. 사용: python -I db_stat_test.py <Server.exe> <Pokemon.db> <결과 파일> <서버 코드 폴더>
import glob, io, json, os, re, socket, sqlite3, subprocess, sys, time

exe, db, out_path, code_dir = sys.argv[1:5]
out = io.open(out_path, "w", encoding="utf-8")
port = 5073
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


KO = {"attack": "공격", "defense": "방어", "special-attack": "특수공격", "special-defense": "특수방어",
      "speed": "스피드", "accuracy": "명중률", "evasion": "회피율"}
SPECIAL = {187, 262, 254, 268, 367, 432}
CONDITIONAL = {563, 575, 579, 597, 599, 601, 602}
rows = con.execute("""SELECT s.move_id, s.stat, s.change, s.target FROM MoveStatChange s JOIN PokemonMove m USING(move_id)
                      WHERE m.category='변화' AND m.generation<=6""").fetchall()
expected = {}
for mid, stat, change, target in rows:
    expected.setdefault(mid, []).append((target, f"{KO[stat]} {change:+d}"))
generic = sorted(m for m in expected if m not in SPECIAL | CONDITIONAL)
p(f"\n[1] DB 능력치 변화로 처리되는 변화기 {len(generic)}개를 하나씩 사용")
learnable = {m for (m,) in con.execute("SELECT DISTINCT l.move_id FROM PokemonLearnset l JOIN Pokemon p USING(pokemon_id) WHERE p.is_selectable=1")}
skipped = []
for mid in generic:
    if mid not in learnable:
        skipped.append(name(mid)); continue
    a = learner([mid]); d = learner(exclude=[a], order="base_hp DESC", without_types=["유령"] if False else ())
    host, guest, hnoop, gnoop = battle(a, [mid], d, [])
    for _ in range(10):
        (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
        if not has(hl, "빗나갔다"): break
    mine = sorted(x for x in changes(host.me()) if x not in ("대타", "기충전"))
    theirs = sorted(changes(host.opp()))
    want_self = sorted(v for t, v in expected[mid] if t == "self")
    want_opp = sorted(v for t, v in expected[mid] if t == "opponent")
    check(mine == want_self and theirs == want_opp, f"{name(mid)}: 자신 {mine} 상대 {theirs} (기대 {want_self} / {want_opp})")
p("   배울 수 있는 포켓몬이 없어 건너뛴 기술:", skipped or "없음")

p("\n[2] 따로 처리하는 기술은 DB 값과 겹쳐 두 번 적용되지 않음")
for mid, want_self, want_opp in ((254, ["방어 +1", "특수방어 +1"], []), (268, ["특수방어 +1"], []), (432, [], ["회피율 -1"])):
    a = learner([mid]); d = learner(exclude=[a], order="base_hp DESC")
    host, guest, hnoop, gnoop = battle(a, [mid], d, [])
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    check(sorted(changes(host.me())) == want_self and sorted(changes(host.opp())) == want_opp,
          f"{name(mid)}: 자신 {changes(host.me())} 상대 {changes(host.opp())}")

p("\n[3] 조건이 있는 6세대 변화기는 아직 미구현으로 표시")
# 막말내뱉기(교체 기술로 구현), 아로마미스트·자기장조작(더블배틀 전용이라 실패 처리)은 따로 구현되어 뺀다.
# 6번 묶음에서 일구기·플라워가드·베놈트랩·지오컨트롤도 구현되어 이제 미구현으로 남은 기술은 없다.
for mid in sorted(CONDITIONAL - {575, 597, 602, 563, 579, 599, 601}):
    if mid not in learnable: continue
    a = learner([mid]); d = learner(exclude=[a], order="base_hp DESC")
    host, guest, hnoop, gnoop = battle(a, [mid], d, [])
    (hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
    check(any(name(mid) in l and "아직 구현되지 않은 기술" in l for l in hl) and not changes(host.me()) and not changes(host.opp()),
          f"{name(mid)}: 미구현 처리")

p("\n[4] 560번 이후 상태이상 (볼부비부비 마비 100%)")
a = learner([609]); d = learner(exclude=[a], order="base_hp DESC", without_types=["전기", "땅"])
host, guest, hnoop, gnoop = battle(a, [609], d, [])
(hl, _), _ = play(host, guest, "USE_MOVE 1", gnoop)
p("  ", hl)
check(host.opp()["Status"] == "마비", "볼부비부비로 상대 마비")

server.kill()
p("\n총 실패:", len(failures))
sys.exit(1 if failures else 0)
