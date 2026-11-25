# 행동 선택 제한시간 인자 테스트. 사용: python -I action_seconds_test.py <Server.exe> <Pokemon.db> <결과 파일> <서버 코드 폴더>
# 서버를 --action-seconds 2 로 띄우면 행동을 고르지 않은 쪽이 2초(+여유 1초) 뒤 자동으로 기술을 고른다.
import io, json, socket, sqlite3, subprocess, sys, time

exe, db, out_path = sys.argv[1:4]
out = io.open(out_path, "w", encoding="utf-8")
port = 5095
con = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
failures = []
PROMPTS = ("CHOOSE_ACTION", "CHOOSE_SWITCH", "WAIT_SWITCH", "LOCKED", "BATTLE_END")
p = lambda *a: print(*a, file=out, flush=True)

def check(cond, msg):
    p(("PASS " if cond else "FAIL ") + msg)
    if not cond:
        failures.append(msg)

server = subprocess.Popen([exe, str(port), "--action-seconds", "2"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
import atexit; atexit.register(server.kill)
for _ in range(75):  # 서버가 포트를 열 때까지 0.2초마다 확인(최대 15초)
    if server.poll() is not None:
        raise SystemExit(f"서버가 바로 종료됨 (종료 코드 {server.returncode})")
    try:
        socket.create_connection(("127.0.0.1", port), timeout=1).close()
        break
    except OSError:
        time.sleep(0.2)
else:
    raise SystemExit(f"서버가 15초 안에 포트 {port}를 열지 않았다")

class Player:
    count = 0
    def __init__(self):
        Player.count += 1
        self.sock = socket.create_connection(("127.0.0.1", port))
        self.sock.settimeout(8.0)
        self.buf = b""
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
                if line.startswith(prefixes):
                    return lines
            self.buf += self.sock.recv(65536)

def member(pid):
    moves = [m for (m,) in con.execute("SELECT move_id FROM PokemonLearnset WHERE pokemon_id=? ORDER BY move_id", (pid,))]
    return {"PokemonId": pid, "MoveIds": moves[:4]}

# 기술이 4개 이상인 팀 (같은 종 없이)
team = [member(pid) for (pid,) in con.execute(
    "SELECT pokemon_id FROM Pokemon p WHERE is_selectable=1 AND pokemon_id IN (143, 242, 113) ORDER BY pokemon_id")]
host, guest = Player(), Player()
host.send("1")
room = host.read_until("CREATED")[-1].split()[1]
guest.send(room); guest.read_until("JOINED")
host.send("1"); host.read_until("READY"); guest.read_until("PLAYER_READY")
guest.send("1")
host.read_until("POKEMON_CATALOG"); guest.read_until("POKEMON_CATALOG")
selection = "SELECT_TEAM " + json.dumps(team, separators=(",", ":"))
host.send(selection); host.read_until("SELECTION_ACCEPTED")
guest.send(selection)
host.read_until(PROMPTS); guest.read_until(PROMPTS)

# 방장만 고르고 참가자는 고르지 않는다.
started = time.monotonic()
host.send("USE_MOVE 1")
guest.sock.settimeout(6.0)
try:
    lines = guest.read_until(PROMPTS)
    elapsed = time.monotonic() - started
    check(any("시간 초과로" in l and "자동 선택했다" in l for l in lines), "고르지 않은 참가자의 기술이 자동으로 정해졌다")
    check(elapsed < 5.0, f"2초 제한시간이 적용됐다 ({elapsed:.1f}초)")
except socket.timeout:
    check(False, "6초 안에 자동 선택이 일어나지 않았다 (--action-seconds 무시)")

p(f"결과: {'모두 통과' if not failures else str(len(failures)) + '개 실패'}")
sys.exit(1 if failures else 0)
