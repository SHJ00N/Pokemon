# 메가진화 X·Y 선택 + 편 효과 표시 테스트. 사용: python -I mega_xy_test.py <Server.exe> <Pokemon.db> <결과 파일>
import io, json, socket, sqlite3, subprocess, sys, time

exe, db, out_path = sys.argv[1:4]
out = io.open(out_path, "w", encoding="utf-8")
port = 5092
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

CHARIZARD, MEGA_X, VENUSAUR, SPLASH = 6, 10034, 3, 150
TYPES = {name: tid for tid, name in con.execute("SELECT type_id, name FROM Type")}

def learnset(pid):
    return [m for (m,) in con.execute("SELECT move_id FROM PokemonLearnset WHERE pokemon_id=? ORDER BY move_id", (pid,))]

def self_boosts(pid):
    """자신의 능력치만 올리는 변화기 (상대를 쓰러뜨리지 않게)"""
    return [m for (m,) in con.execute(
        "SELECT DISTINCT s.move_id FROM MoveStatChange s JOIN PokemonMove m USING(move_id) "
        "WHERE s.target='self' AND m.category='변화' ORDER BY s.move_id") if m in learnset(pid)]

def member(pid, moves=None):
    moves = moves or (self_boosts(pid) + [m for m in learnset(pid) if m not in self_boosts(pid)])
    return {"PokemonId": pid, "MoveIds": list(dict.fromkeys(moves))[:4]}

def splasher(exclude):
    for (pid,) in con.execute("SELECT pokemon_id FROM Pokemon WHERE is_selectable=1 ORDER BY base_hp DESC"):
        species = con.execute("SELECT species_id FROM Pokemon WHERE pokemon_id=?", (pid,)).fetchone()[0]
        if species not in exclude and SPLASH in learnset(pid) and len(learnset(pid)) >= 4 and \
                not con.execute("SELECT 1 FROM PokemonFormChange WHERE base_pokemon_id=? AND method='mega'", (pid,)).fetchone():
            return pid, species

class Player:
    count = 0
    def __init__(self):
        Player.count += 1
        self.sock = socket.create_connection(("127.0.0.1", port))
        self.sock.settimeout(8.0)
        self.buf = b""
        self.state = None
        self.send(f"NAME m{Player.count}")
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
        return lines, [l[11:] for l in lines if l.startswith("BATTLE_LOG ")]

    def me(self): return self.state["Team"][self.state["Active"]]["Pokemon"]
    def opp(self): return self.state["Opponent"]

def error_of(pl, command):
    pl.send(command)
    pl.sock.settimeout(0.5)
    try:
        while b"\n" not in pl.buf:
            pl.buf += pl.sock.recv(65536)
    except socket.timeout:
        pass
    finally:
        pl.sock.settimeout(8.0)
    if pl.buf.startswith(b"ERROR"):
        line, pl.buf = pl.buf.split(b"\n", 1)
        return line.decode("utf-8")
    return None

def start(host_team, guest_team):
    host, guest = Player(), Player()
    host.send("1")
    room = host.read_until("CREATED")[-1].split()[1]
    guest.send(room); guest.read_until("JOINED")
    host.send("1"); guest.send("1")
    host.read_until("POKEMON_CATALOG"); guest.read_until("POKEMON_CATALOG")
    host.send("SELECT_TEAM " + json.dumps(host_team, separators=(",", ":"))); guest.send("SELECT_TEAM " + json.dumps(guest_team, separators=(",", ":")))
    for pl, team in ((guest, guest_team), (host, host_team)):
        lines = pl.read_until(PROMPTS + ("ERROR",))
        if lines[-1].startswith("ERROR"):
            p("   선택 오류:", lines[-1], team)
            raise SystemExit(1)
    return host, guest

MEGA_Y, REFLECT = 10035, 115

def reflector(exclude):
    for (pid,) in con.execute("SELECT pokemon_id FROM Pokemon WHERE is_selectable=1 ORDER BY base_hp DESC"):
        species = con.execute("SELECT species_id FROM Pokemon WHERE pokemon_id=?", (pid,)).fetchone()[0]
        if species not in exclude and REFLECT in learnset(pid) and len(learnset(pid)) >= 4 and \
                not con.execute("SELECT 1 FROM PokemonFormChange WHERE base_pokemon_id=? AND method='mega'", (pid,)).fetchone():
            return pid, species

r1, rs1 = reflector({CHARIZARD, VENUSAUR})
g2, s2 = splasher({CHARIZARD, VENUSAUR, rs1})
g3, _ = splasher({CHARIZARD, VENUSAUR, rs1, s2})
host, guest = start([member(CHARIZARD), member(VENUSAUR), member(g3)],
                    [member(r1, [REFLECT] + learnset(r1)), member(g2), member(g3)])
forms = [f["PokemonId"] for f in host.state.get("MegaForms") or []]
check(forms == [MEGA_X, MEGA_Y], f"리자몽 메가 폼 목록에 X·Y 둘 다 ({host.state.get('MegaForms')})")
check(not guest.state.get("MegaForms"), "메가진화 못 하는 쪽은 폼 목록이 비어 있음")
e = error_of(host, "USE_MOVE 1 MEGA 99999")
check(e is not None and "폼" in e, f"목록에 없는 폼 번호는 거절: {e}")
e = error_of(host, "USE_MOVE 1 MEGA abc")
check(e is not None, f"형식이 틀린 메가진화 요청은 거절: {e}")
host.send(f"USE_MOVE 1 MEGA {MEGA_Y}"); guest.send("USE_MOVE 1")
(hlines, hl), (glines, gl) = host.turn(), guest.turn()
p("   1턴 로그:", hl)
check(host.me()["PokemonId"] == MEGA_Y and guest.opp()["PokemonId"] == MEGA_Y, f"고른 메가리자몽Y가 됨 ({host.me()['PokemonId']})")
check([t["Name"] for t in host.me()["Types"]] == ["불꽃", "비행"], f"메가리자몽Y 타입 {[t['Name'] for t in host.me()['Types']]}")
check(any(l.startswith("메가리자몽Y로") or "메가리자몽Y로 메가진화" in l for l in hl), "메가진화 로그에 Y 폼 이름")
check(any(x.startswith("리플렉터") for x in guest.state.get("SideEffects") or []), f"리플렉터 쓴 쪽 SideEffects: {guest.state.get('SideEffects')}")
check(any(x.startswith("리플렉터") for x in host.state.get("OpponentSideEffects") or []), f"상대 화면 OpponentSideEffects: {host.state.get('OpponentSideEffects')}")
check(not host.state.get("SideEffects"), f"리플렉터 안 쓴 쪽 SideEffects 비어 있음: {host.state.get('SideEffects')}")
check(not host.state.get("MegaForms") and host.state.get("CanMegaEvolve") is False, "메가진화 후 폼 목록·선택지 사라짐")

p("결과:", "모두 통과" if not failures else f"실패 {len(failures)}개")
sys.exit(1 if failures else 0)
