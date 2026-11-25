# 메가진화 테스트. 사용: python -I mega_test.py <Server.exe> <Pokemon.db> <결과 파일>
import io, json, socket, sqlite3, subprocess, sys, time

exe, db, out_path = sys.argv[1:4]
out = io.open(out_path, "w", encoding="utf-8")
port = 5091
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

g1, s1 = splasher({CHARIZARD, VENUSAUR})
g2, s2 = splasher({CHARIZARD, VENUSAUR, s1})
g3, _ = splasher({CHARIZARD, VENUSAUR, s1, s2})
host, guest = start([member(CHARIZARD), member(VENUSAUR), member(g3)],
                    [member(g1, [SPLASH] + learnset(g1)), member(g2), member(g3)])
check(host.state.get("CanMegaEvolve") is True, "메가진화할 수 있는 포켓몬이 나오면 CanMegaEvolve = true")
check(guest.state.get("CanMegaEvolve") is False, "메가진화할 수 없는 포켓몬은 false")
types_before = [t["Name"] for t in host.me()["Types"]]

# 1턴: 메가진화를 고르고 기술 사용
host.send("USE_MOVE 1 MEGA"); guest.send("USE_MOVE 1")
(hlines, hl), (glines, gl) = host.turn(), guest.turn()
check(any(l.startswith("BATTLE_MEGA SELF ") for l in hlines), "내 화면에 BATTLE_MEGA SELF")
check(any(l.startswith("BATTLE_MEGA OPPONENT ") for l in glines), "상대 화면에 BATTLE_MEGA OPPONENT")
mega_i = next((i for i, l in enumerate(hl) if "메가진화했다" in l), None)
move_i = next((i for i, l in enumerate(hl) if l.startswith("리자몽의 ") or l.startswith("메가리자몽")), None)
check(mega_i is not None, f"메가진화 로그: {hl}")
check(host.me()["PokemonId"] == MEGA_X and guest.opp()["PokemonId"] == MEGA_X, f"메가리자몽X가 됨 ({host.me()['PokemonId']})")
types_after = [t["Name"] for t in host.me()["Types"]]
check(types_after != types_before and "드래곤" in types_after, f"타입이 메가 폼으로 바뀜 {types_before} → {types_after}")
check(host.state.get("CanMegaEvolve") is False, "메가진화 후에는 선택지가 사라짐")
p("   1턴 로그:", hl)

# 2턴: 교체 → 메가진화할 수 있는 이상해꽃이 나와도 이미 썼으므로 선택지 없음
host.send("SWITCH 2"); guest.send("USE_MOVE 1")
host.turn(); guest.turn()
check(host.me()["SpeciesId"] == VENUSAUR and host.state.get("CanMegaEvolve") is False, "한 전투에 한 번: 다른 포켓몬은 메가진화 불가")
e = error_of(host, "USE_MOVE 1 MEGA")
check(e is not None and "메가진화" in e, f"메가진화를 다시 보내면 거절: {e}")
check(host.state["Team"][0]["Pokemon"]["PokemonId"] == MEGA_X, "들어가 있는 동안에도 팀 목록에는 메가리자몽X")
host.send("USE_MOVE 1"); guest.send("USE_MOVE 1")
host.turn(); guest.turn()

# 3턴: 다시 리자몽으로 교체 → 메가 폼 유지
host.send("SWITCH 1"); guest.send("USE_MOVE 1")
host.turn(); guest.turn()
check(host.me()["PokemonId"] == MEGA_X and "드래곤" in [t["Name"] for t in host.me()["Types"]],
      f"교체해서 다시 나와도 메가 폼 유지 ({host.me()['PokemonId']}, {[t['Name'] for t in host.me()['Types']]})")
check(guest.opp()["PokemonId"] == MEGA_X, "상대 화면에도 메가 폼")

# 메가진화하지 않아도 된다
host2, guest2 = start([member(CHARIZARD), member(VENUSAUR), member(g3)], [member(g1, [SPLASH] + learnset(g1)), member(g2), member(g3)])
host2.send("USE_MOVE 1"); guest2.send("USE_MOVE 1")
host2.turn(); guest2.turn()
check(host2.me()["PokemonId"] == CHARIZARD and host2.state.get("CanMegaEvolve") is True, "고르지 않으면 메가진화하지 않고 선택지가 남음")

# 레쿠쟈는 화룡점정을 알고 있어야 메가진화할 수 있다.
RAYQUAZA, MEGA_RAYQUAZA, DRAGON_ASCENT = 384, 10079, 620
others = [m for m in self_boosts(RAYQUAZA) + learnset(RAYQUAZA) if m != DRAGON_ASCENT]
guest_team = [member(g1, [SPLASH] + learnset(g1)), member(g2), member(g3)]
host3, guest3 = start([member(RAYQUAZA, others), member(VENUSAUR), member(g3)], guest_team)
check(host3.state.get("CanMegaEvolve") is False, "화룡점정이 없는 레쿠쟈는 메가진화 선택지 없음")
e = error_of(host3, "USE_MOVE 1 MEGA")
check(e is not None and "메가진화" in e, f"화룡점정 없이 메가진화를 보내면 거절: {e}")
host4, guest4 = start([member(RAYQUAZA, [DRAGON_ASCENT] + others), member(VENUSAUR), member(g3)], guest_team)
check(host4.state.get("CanMegaEvolve") is True, "화룡점정을 고른 레쿠쟈는 메가진화 선택지 있음")
host4.send("USE_MOVE 2 MEGA"); guest4.send("USE_MOVE 1")
host4.turn(); guest4.turn()
check(host4.me()["PokemonId"] == MEGA_RAYQUAZA, f"메가레쿠쟈로 메가진화 ({host4.me()['PokemonId']})")

# 6세대: 턴이 시작될 때(교체 다음) 기술보다 먼저 메가진화한다. 행동 순서는 메가진화 전 스피드로 정한다.
EARTHQUAKE, BEEDRILL, MEGA_BEEDRILL = 89, 15, 10090
def pick(where, exclude, required=()):
    for (pid,) in con.execute(f"SELECT pokemon_id FROM Pokemon p WHERE is_selectable=1 AND {where} "
                              "AND pokemon_id NOT IN (SELECT base_pokemon_id FROM PokemonFormChange WHERE method='mega') ORDER BY base_hp DESC"):
        species = con.execute("SELECT species_id FROM Pokemon WHERE pokemon_id=?", (pid,)).fetchone()[0]
        if species not in exclude and all(m in learnset(pid) for m in required) and len(learnset(pid)) >= 4:
            return pid, species
    raise RuntimeError(where)

fast_ground, fg_species = pick("base_speed > 110", {CHARIZARD, VENUSAUR}, [EARTHQUAKE])
host5, guest5 = start([member(CHARIZARD), member(VENUSAUR), member(g3)],
                      [member(fast_ground, [EARTHQUAKE] + learnset(fast_ground)), member(g2), member(g3)])
host5.send("USE_MOVE 1 MEGA"); guest5.send("USE_MOVE 1")
(_, hl), _ = host5.turn(), guest5.turn()
p("   빠른 상대의 지진:", hl)
mega_at = next((i for i, l in enumerate(hl) if "메가진화했다" in l), 99)
quake_at = next((i for i, l in enumerate(hl) if l.startswith("상대 ") and "지진" in l), -1)
check(0 <= mega_at < quake_at, "상대가 더 빨라도 메가진화가 먼저 일어남")
check(quake_at >= 0 and "효과가 없" not in hl[quake_at] and "데미지" in hl[quake_at],
      "비행 타입이 빠진 메가리자몽X로 지진을 맞음 (메가진화 전이면 효과 없음)")

mid_speed, ms_species = pick("base_speed BETWEEN 90 AND 130", {BEEDRILL, VENUSAUR})
guest_moves = self_boosts(mid_speed) + [m for m in learnset(mid_speed) if m not in self_boosts(mid_speed)]
host6, guest6 = start([member(BEEDRILL), member(VENUSAUR), member(g3)],
                      [member(mid_speed, guest_moves), member(g2), member(g3)])
host6.send("USE_MOVE 1 MEGA"); guest6.send("USE_MOVE 1")
(_, hl), _ = host6.turn(), guest6.turn()
p("   독침붕 순서:", hl)
mega_at = next((i for i, l in enumerate(hl) if "메가진화했다" in l), 99)
own_at = next((i for i, l in enumerate(hl) if l.startswith("메가독침붕의 ")), -1)
opp_at = next((i for i, l in enumerate(hl) if l.startswith("상대 ") and "의 " in l), -1)
check(host6.me()["PokemonId"] == MEGA_BEEDRILL, "메가독침붕이 됨")
check(mega_at < opp_at < own_at, "이번 턴 순서는 메가진화 전 스피드: 메가진화 → 상대 → 메가독침붕")
host6.send("USE_MOVE 1"); guest6.send("USE_MOVE 1")
(_, hl), _ = host6.turn(), guest6.turn()
own_at = next((i for i, l in enumerate(hl) if l.startswith("메가독침붕의 ")), 99)
opp_at = next((i for i, l in enumerate(hl) if l.startswith("상대 ") and "의 " in l), -1)
check(own_at < opp_at, f"다음 턴부터는 메가 폼 스피드로 먼저 행동: {hl[:3]}")

p(f"결과: {'모두 통과' if not failures else str(len(failures)) + '개 실패'}")
