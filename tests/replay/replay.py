# 녹화 비교: 시드를 고정한 대본 대전에서 서버가 두 플레이어에게 보낸 줄을 모두 저장(record)하거나,
# 저장해 둔 기준과 한 글자씩 비교(check)한다. 전투 코드를 옮기거나 고친 뒤 1:1 동작이 그대로인지 확인하는 용도.
# 사용: python replay.py record|check <Server.exe> [--only 대본,대본]
import argparse, json, os, select, socket, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)   # -I로 실행해도 같은 폴더의 scenarios.py를 읽는다
import scenarios

PORT = 5094
SEED = 7
ACTION_SECONDS = 3          # 시간 초과 대본이 오래 기다리지 않게 행동 제한시간을 줄인다
SCENARIO_TIMEOUT = 60       # 대본 하나가 이보다 오래 걸리면 실패
BASELINE = os.path.join(HERE, "baseline")
INPUT_PROMPTS = {"CHOOSE_ACTION", "CHOOSE_SWITCH", "CHOOSE_SWITCH_MIDTURN"}


class ScenarioError(Exception):
    pass


def start_server(exe):
    server = subprocess.Popen([exe, str(PORT), "--seed", str(SEED), "--action-seconds", str(ACTION_SECONDS)],
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    for _ in range(75):  # 서버가 포트를 열 때까지 0.2초마다 확인(최대 15초)
        if server.poll() is not None:
            raise ScenarioError(f"서버가 바로 종료됨 (종료 코드 {server.returncode})")
        try:
            socket.create_connection(("127.0.0.1", PORT), timeout=1).close()
            return server
        except OSError:
            time.sleep(0.2)
    server.kill()
    raise ScenarioError(f"서버가 15초 안에 포트 {PORT}를 열지 않았다")


def stop_server(server):
    server.kill()
    server.wait()  # 다음 대본이 같은 포트를 쓰므로 완전히 끝날 때까지 기다린다


class Player:
    """한 플레이어의 접속. 받은 줄을 모두 기록하고, 입력을 요구하는 안내를 받으면 대본의 다음 명령을 보낸다.

    명령: 서버 명령 그대로(USE_MOVE 1, SWITCH 2, SURRENDER, REMATCH ...) 또는
      WAIT              이번 안내에는 아무것도 보내지 않는다(시간 초과 자동 선택이나 상대의 항복을 기다림)
      EXPECT_ERROR 명령  명령을 보내고 ERROR 한 줄을 받은 뒤, 같은 안내에 대해 다음 명령을 이어서 처리한다
      DISCONNECT        접속을 끊는다
      @접두어 명령       그 접두어로 시작하는 줄을 받은 뒤에 명령을 보낸다(재도전 신청 순서를 고정할 때)
      AUTO*             이후 모든 안내에 자동으로 답한다: 기술 1번, 교체는 쓰러지지 않은 첫 포켓몬. 전투가 끝나면 끝
    명령이 다 떨어진 상태에서 안내를 받으면 그 플레이어의 녹화를 끝낸다."""

    def __init__(self, name, team, commands):
        self.name, self.team, self.commands = name, team, list(commands)
        self.sock = socket.create_connection(("127.0.0.1", PORT))
        self.sock.settimeout(10)
        self.buf = b""
        self.log = []
        self.state = None       # 마지막 BATTLE_STATE
        self.auto = False
        self.gate = None        # (접두어, 명령)
        self.done = False

    def send(self, text):
        self.sock.sendall((text + "\n").encode())

    def next_line(self):
        while b"\n" not in self.buf:
            data = self.sock.recv(65536)
            if not data:
                raise ScenarioError(f"{self.name}: 서버가 접속을 끊었다")
            self.buf += data
        line, self.buf = self.buf.split(b"\n", 1)
        return line.decode("utf-8").rstrip("\r")

    def record(self, line):
        # 포켓몬 목록은 수십 KB이고 매번 같으므로 길이만 남긴다.
        self.log.append(f"POKEMON_CATALOG <{len(line)}자>" if line.startswith("POKEMON_CATALOG ") else line)
        if line.startswith("BATTLE_STATE "):
            self.state = json.loads(line[len("BATTLE_STATE "):])

    def read_until(self, prefix):
        while True:
            line = self.next_line()
            self.record(line)
            if line.startswith(prefix):
                return line

    def select_team(self):
        self.send("SELECT_TEAM " + json.dumps(self.team, separators=(",", ":")))

    def handle(self, line):
        self.record(line)
        if self.gate and line.startswith(self.gate[0]):
            command, self.gate = self.gate[1], None
            self.send(command)
        elif line == "REMATCH_STARTED":
            self.select_team()
        elif line in INPUT_PROMPTS or line.startswith("BATTLE_END"):
            self.respond(line)

    def respond(self, prompt):
        while True:
            if self.auto:
                if prompt.startswith("BATTLE_END"):
                    self.done = True
                else:
                    self.send(self.auto_command(prompt))
                return
            if not self.commands:
                self.done = True
                return
            command = self.commands.pop(0)
            if command == "WAIT":
                return
            if command == "AUTO*":
                self.auto = True
                continue
            if command == "DISCONNECT":
                self.sock.close()
                self.done = True
                return
            if command.startswith("@"):
                prefix, rest = command[1:].split(" ", 1)
                self.gate = (prefix, rest)
                return
            if command.startswith("EXPECT_ERROR "):
                self.send(command[len("EXPECT_ERROR "):])
                line = self.next_line()
                self.record(line)
                if not line.startswith("ERROR"):
                    raise ScenarioError(f"{self.name}: {command} 다음에 ERROR가 아닌 줄: {line[:120]}")
                continue
            self.send(command)
            return

    def auto_command(self, prompt):
        if prompt == "CHOOSE_ACTION":
            return "USE_MOVE 1"
        team, active = self.state["Team"], self.state["Active"]
        for index, member in enumerate(team):
            if index != active and member["Pokemon"]["CurrentHp"] > 0:
                return f"SWITCH {index + 1}"
        raise ScenarioError(f"{self.name}: 교체할 포켓몬이 없는데 {prompt}를 받았다")

    def pump(self):
        """버퍼에 이미 들어온 줄을 처리한다."""
        while b"\n" in self.buf and not self.done:
            line, self.buf = self.buf.split(b"\n", 1)
            self.handle(line.decode("utf-8").rstrip("\r"))


def play(exe, scenario):
    server = start_server(exe)
    try:
        host = Player("h", scenario["host_team"], scenario["host"])
        guest = Player("g", scenario["guest_team"], scenario["guest"])
        # 대기실 단계는 한쪽씩 차례로 진행한다. 동시에 보내면 서버에 먼저 닿는 쪽에 따라 메시지 순서가 바뀐다.
        host.send("NAME h"); host.read_until("NAME_ACCEPTED")
        guest.send("NAME g"); guest.read_until("NAME_ACCEPTED")
        host.send("1")
        room = host.read_until("CREATED").split()[1]
        guest.send(room); guest.read_until("JOINED")
        host.send("1"); host.read_until("READY"); guest.read_until("PLAYER_READY")
        guest.send("1")
        host.read_until("POKEMON_CATALOG"); guest.read_until("POKEMON_CATALOG")
        host.select_team(); host.read_until("SELECTION_ACCEPTED")
        guest.select_team()

        deadline = time.monotonic() + SCENARIO_TIMEOUT
        players = (host, guest)
        while True:
            for player in players:
                if not player.done:
                    player.pump()
            live = [player for player in players if not player.done]
            if not live:
                return host.log, guest.log
            if time.monotonic() > deadline:
                raise ScenarioError("대본이 끝나지 않음: " + ", ".join(
                    f"{p.name} 남은 명령 {p.commands} 마지막 줄 {p.log[-1][:80] if p.log else ''}" for p in live))
            ready, _, _ = select.select([p.sock for p in live], [], [], 0.5)
            for player in live:
                if player.sock in ready:
                    data = player.sock.recv(65536)
                    if not data:
                        raise ScenarioError(f"{player.name}: 서버가 접속을 끊었다")
                    player.buf += data
    finally:
        stop_server(server)


def baseline_path(name, who):
    return os.path.join(BASELINE, f"{name}.{who}.txt")


def missing_expectations(scenario, logs):
    missing = []
    for expectation in scenario.get("expect", []):
        who, text = expectation.split(":", 1)
        if not any(text in line for line in logs[who]):
            missing.append(expectation)
    return missing


def first_difference(expected, actual):
    for index, (a, b) in enumerate(zip(expected, actual)):
        if a != b:
            return f"{index + 1}번째 줄\n    기준: {a[:200]}\n    지금: {b[:200]}"
    return f"줄 수가 다름 (기준 {len(expected)}줄, 지금 {len(actual)}줄)"


def main():
    sys.stdout.reconfigure(encoding="utf-8")   # -I로 실행하면 PYTHONUTF8이 무시되므로 직접 맞춘다
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["record", "check"])
    ap.add_argument("exe")
    ap.add_argument("--only", default="")
    args = ap.parse_args()
    args.exe = os.path.abspath(args.exe)   # Windows에서 / 가 들어간 상대 경로는 실행되지 않는다
    only = {n.strip() for n in args.only.split(",") if n.strip()}
    known = {s["name"] for s in scenarios.SCENARIOS}
    if only - known:
        sys.exit("없는 대본: " + ", ".join(sorted(only - known)))
    os.makedirs(BASELINE, exist_ok=True)

    failed = 0
    for scenario in scenarios.SCENARIOS:
        name = scenario["name"]
        if only and name not in only:
            continue
        try:
            host_log, guest_log = play(args.exe, scenario)
        except (ScenarioError, OSError) as error:
            print(f"{name}: 실패 - {error}", flush=True)
            failed += 1
            continue
        logs = {"h": host_log, "g": guest_log}
        problems = [f"기대한 문구 없음: {m}" for m in missing_expectations(scenario, logs)]
        for who, log in (("host", host_log), ("guest", guest_log)):
            path = baseline_path(name, who)
            if args.mode == "record":
                with open(path, "w", encoding="utf-8", newline="\n") as f:
                    f.write("\n".join(log) + "\n")
            else:
                if not os.path.exists(path):
                    problems.append(f"{who}: 기준 파일 없음 ({path})")
                    continue
                expected = open(path, encoding="utf-8").read().splitlines()
                if expected != log:
                    problems.append(f"{who}: 기준과 다름 - {first_difference(expected, log)}")
        if problems:
            failed += 1
            print(f"{name}: 실패", flush=True)
            for problem in problems:
                print("  " + problem, flush=True)
        else:
            print(f"{name}: {'녹화' if args.mode == 'record' else '같음'} (방장 {len(host_log)}줄, 참가자 {len(guest_log)}줄)",
                  flush=True)
    print("녹화 비교: " + ("통과" if not failed else f"실패 {failed}개 대본"))
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
