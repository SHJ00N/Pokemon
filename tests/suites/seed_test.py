# 시드 테스트. 사용: python -I seed_test.py <Server.exe> <Pokemon.db> <결과 파일> <서버 코드 폴더>
# 서버를 --seed 로 여러 번 띄워 같은 대전의 메시지를 비교한다.
import io, json, socket, sqlite3, subprocess, sys, time

exe, db, out_path = sys.argv[1:4]
out = io.open(out_path, "w", encoding="utf-8")
port = 5093
con = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
failures = []
PROMPTS = ("CHOOSE_ACTION", "CHOOSE_SWITCH", "WAIT_SWITCH", "LOCKED", "BATTLE_END")
p = lambda *a: print(*a, file=out, flush=True)

def check(cond, msg):
    p(("PASS " if cond else "FAIL ") + msg)
    if not cond:
        failures.append(msg)

def start_server(seed):
    server = subprocess.Popen([exe, str(port), "--seed", str(seed)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    for _ in range(75):  # 서버가 포트를 열 때까지 0.2초마다 확인(최대 15초)
        if server.poll() is not None:
            raise SystemExit(f"서버가 바로 종료됨 (종료 코드 {server.returncode})")
        try:
            socket.create_connection(("127.0.0.1", port), timeout=1).close()
            return server
        except OSError:
            time.sleep(0.2)
    server.kill()
    raise SystemExit(f"서버가 15초 안에 포트 {port}를 열지 않았다")

def stop_server(server):
    server.kill()
    server.wait()  # 다음 서버가 같은 포트를 쓰므로 완전히 끝날 때까지 기다린다

class Player:
    count = 0
    def __init__(self):
        Player.count += 1
        self.sock = socket.create_connection(("127.0.0.1", port))
        self.sock.settimeout(8.0)
        self.buf = b""
        self.log = []  # 받은 줄 전체
        self.send(f"NAME s{Player.count}")
        self.read_until("NAME_ACCEPTED")

    def send(self, text): self.sock.sendall((text + "\n").encode())

    def read_until(self, prefixes):
        lines = []
        while True:
            while b"\n" in self.buf:
                line, self.buf = self.buf.split(b"\n", 1)
                line = line.decode("utf-8")
                lines.append(line)
                self.log.append(line)
                if line.startswith(prefixes):
                    return lines
            self.buf += self.sock.recv(65536)

def learnset(pid):
    return [m for (m,) in con.execute("SELECT move_id FROM PokemonLearnset WHERE pokemon_id=? ORDER BY move_id", (pid,))]

def make_team():
    """체력이 높은 포켓몬 3마리. 첫 기술은 명중이 100 미만인 약한 공격 기술(난수·급소·명중이 모두 들어간다)."""
    attack = con.execute("SELECT move_id FROM PokemonMove WHERE category <> '변화' AND power BETWEEN 40 AND 60 "
                         "AND accuracy BETWEEN 80 AND 95 AND pp >= 15 AND move_kind='normal' ORDER BY move_id").fetchone()[0]
    team = []
    for (pid,) in con.execute("SELECT pokemon_id FROM Pokemon WHERE is_selectable=1 ORDER BY base_hp DESC, pokemon_id"):
        moves = learnset(pid)
        if attack in moves and len(moves) >= 4:
            team.append({"PokemonId": pid, "MoveIds": [attack] + [m for m in moves if m != attack][:3]})
        if len(team) == 3:
            return team
    raise RuntimeError("팀을 만들 수 없다")

TEAM = make_team()

class Battle:
    """방 하나: 방장과 참가자가 있다."""
    def __init__(self):
        self.host, self.guest = Player(), Player()
        self.host.send("1")
        self.room = int(self.host.read_until("CREATED")[-1].split()[1])

    def begin(self):
        host, guest = self.host, self.guest
        guest.send(str(self.room)); guest.read_until("JOINED")
        # 먼저 보낸 쪽의 처리가 끝난 뒤에 보낸다. 동시에 보내면 서버에 먼저 닿는 쪽에 따라 READY/PLAYER_READY 순서가 바뀐다.
        host.send("1"); host.read_until("READY"); guest.read_until("PLAYER_READY")
        guest.send("1")
        host.read_until("POKEMON_CATALOG"); guest.read_until("POKEMON_CATALOG")
        team = "SELECT_TEAM " + json.dumps(TEAM, separators=(",", ":"))
        host.send(team); host.read_until("SELECTION_ACCEPTED")
        guest.send(team)
        guest.read_until(PROMPTS + ("ERROR",)); host.read_until(PROMPTS + ("ERROR",))

    def turn(self):
        self.host.send("USE_MOVE 1"); self.guest.send("USE_MOVE 1")
        self.host.read_until(PROMPTS); self.guest.read_until(PROMPTS)

def run(seed, mode):
    """mode: 'one' = 방 하나로 5턴, 'two' = 방 둘을 번갈아 5턴씩, 'second' = 방 하나는 만들어 두기만 하고 둘째 방만 5턴.
    돌려주는 값: 진행한 방의 (방 번호, 방장이 받은 줄, 참가자가 받은 줄)"""
    Player.count = 0  # 닉네임이 매번 같도록
    server = start_server(seed)
    try:
        a = Battle()
        if mode == "one":
            a.begin()
            for _ in range(5): a.turn()
            return a.room, a.host.log, a.guest.log
        b = Battle()
        if mode == "two":
            a.begin(); b.begin()
            for _ in range(5):
                a.turn(); b.turn()
        else:
            b.begin()
            for _ in range(5): b.turn()
        return b.room, b.host.log, b.guest.log
    finally:
        stop_server(server)

def first_diff(a, b):
    for i, (x, y) in enumerate(zip(a, b)):
        if x != y:
            return f"{i}번째 줄: {x[:200]!r} / {y[:200]!r}"
    return f"길이 {len(a)} / {len(b)}"

def test_same_seed_same_messages():
    _, h1, g1 = run(42, "one")
    _, h2, g2 = run(42, "one")
    check(any(l.startswith("BATTLE_LOG ") for l in h1), "5턴 동안 전투 로그가 오갔다")
    check(h1 == h2, f"시드 42 두 번: 방장 메시지 {len(h1)}줄이 완전히 같다" + ("" if h1 == h2 else " " + first_diff(h1, h2)))
    check(g1 == g2, f"시드 42 두 번: 참가자 메시지 {len(g1)}줄이 완전히 같다" + ("" if g1 == g2 else " " + first_diff(g1, g2)))

def test_different_seed_differs():
    _, h1, g1 = run(42, "one")
    _, h2, g2 = run(43, "one")
    check(h1 != h2 or g1 != g2, "시드 42와 43은 메시지가 한 줄 이상 다르다")

def test_two_rooms_independent():
    r_two, h_two, g_two = run(42, "two")
    r_sec, h_sec, g_sec = run(42, "second")
    check(r_two == 1001 and r_sec == 1001, f"두 번째 방 번호는 1001 ({r_two}, {r_sec})")
    # 접속 순서가 달라 닉네임이 다르므로 전투 메시지만 비교한다.
    battle = lambda lines: [l for l in lines if l.startswith("BATTLE_")]
    check(battle(h_two) == battle(h_sec) and len(battle(h_two)) > 0, "방 둘을 번갈아 진행해도 둘째 방 전투(방장 쪽)가 같다")
    check(battle(g_two) == battle(g_sec) and len(battle(g_two)) > 0, "방 둘을 번갈아 진행해도 둘째 방 전투(참가자 쪽)가 같다")

test_same_seed_same_messages()
test_different_seed_differs()
test_two_rooms_independent()
p(f"결과: {'모두 통과' if not failures else str(len(failures)) + '개 실패'}")
