# 메타몽 팀 선택·변신, 루브도·안농 막기, 내 편·상대 편 효과 표시 테스트
# 사용: python -I ditto_side_test.py <Server.exe> <Pokemon.db> <결과 파일>
import io, json, socket, sqlite3, subprocess, sys, time

exe, db, out_path = sys.argv[1:4]
out = io.open(out_path, "w", encoding="utf-8")
port = 5088
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

DITTO, SMEARGLE, UNOWN = 132, 235, 201
TRANSFORM, SKETCH, HIDDEN_POWER = 144, 166, 237
REFLECT, STEALTH_ROCK, SPIKES, SPLASH = 115, 446, 191, 150

def learnset(pid):
    return [m for (m,) in con.execute("SELECT move_id FROM PokemonLearnset WHERE pokemon_id=? ORDER BY move_id", (pid,))]

def find(required, exclude=()):
    for (pid,) in con.execute("SELECT pokemon_id FROM Pokemon WHERE is_selectable=1 ORDER BY base_hp DESC, pokemon_id"):
        species = con.execute("SELECT species_id FROM Pokemon WHERE pokemon_id=?", (pid,)).fetchone()[0]
        if species in exclude: continue
        ls = learnset(pid)
        if len(ls) >= 4 and all(m in ls for m in required):
            return pid, species
    raise RuntimeError(f"no learner {required}")

def member(pid, first):
    moves = list(first) + [m for m in learnset(pid) if m not in first]
    return {"PokemonId": pid, "MoveIds": moves[:4]}

class Player:
    count = 0
    def __init__(self):
        Player.count += 1
        self.sock = socket.create_connection(("127.0.0.1", port))
        self.sock.settimeout(8.0)
        self.buf = b""
        self.state = None
        self.send(f"NAME d{Player.count}")
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

    def active(self): return self.state["Team"][self.state["Active"]]

def enter_game():
    host, guest = Player(), Player()
    host.send("1")
    room = host.read_until("CREATED")[-1].split()[1]
    guest.send(room); guest.read_until("JOINED")
    host.send("1"); guest.send("1")
    host.read_until("POKEMON_CATALOG"); guest.read_until("POKEMON_CATALOG")
    return host, guest

def select(pl, team):
    """팀을 보낸다. 거절되면 ERROR 문구, 받아들여지면 None"""
    pl.send("SELECT_TEAM " + json.dumps(team, separators=(",", ":")))
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

# ── 1. 팀 선택 ──
f1, s1 = find([SPLASH], exclude={DITTO, SMEARGLE, UNOWN})
f2, s2 = find([], exclude={DITTO, SMEARGLE, UNOWN, s1})
fillers = [member(f1, [SPLASH]), member(f2, [])]
host, guest = enter_game()
check(select(host, [{"PokemonId": SMEARGLE, "MoveIds": [SKETCH]}] + fillers) is not None, "루브도(스케치 하나)는 팀에 넣을 수 없다")
check(select(host, [{"PokemonId": UNOWN, "MoveIds": [HIDDEN_POWER]}] + fillers) is not None, "안농(잠재파워 하나)은 팀에 넣을 수 없다")
check(select(host, [{"PokemonId": DITTO, "MoveIds": [TRANSFORM, TRANSFORM]}] + fillers) is not None, "메타몽에 같은 기술 두 번은 거절")
check(select(host, [{"PokemonId": DITTO, "MoveIds": []}] + fillers) is not None, "메타몽에 기술 0개는 거절")
check(select(host, [{"PokemonId": f1, "MoveIds": learnset(f1)[:3]}, fillers[1], {"PokemonId": DITTO, "MoveIds": [TRANSFORM]}]) is not None,
      "4개 넘게 배우는 포켓몬을 기술 3개로 고르면 거절")
check(select(host, [{"PokemonId": DITTO, "MoveIds": [TRANSFORM]}] + fillers) is None, "메타몽은 변신 하나로 팀에 넣을 수 있다")

# ── 2. 메타몽 전투: 변신하면 상대 기술을 그대로 쓴다 ──
attacker, a_species = find([SPLASH], exclude={DITTO, SMEARGLE, UNOWN, s1, s2})
g1, gs1 = find([], exclude={DITTO, SMEARGLE, UNOWN, a_species})
g2, _ = find([], exclude={DITTO, SMEARGLE, UNOWN, a_species, gs1})
guest_team = [member(attacker, [SPLASH]), member(g1, []), member(g2, [])]
check(select(guest, guest_team) is None, "상대 팀 선택")
host.turn(); guest.turn()
ditto = host.active()
check([m["MoveId"] for m in ditto["Moves"]] == [TRANSFORM], f"메타몽의 기술은 변신 하나: {[m['MoveId'] for m in ditto['Moves']]}")
e = error_of(host, "USE_MOVE 2")
check(e is not None and "슬롯" in e, f"기술이 하나뿐이면 2번 기술은 고를 수 없다: {e}")
host.send("USE_MOVE 1"); guest.send("USE_MOVE 1")    # 메타몽 변신, 상대 튀어오르기
(hl, _), (gl, _) = host.turn(), guest.turn()
check(any("변신했다" in l for l in hl), f"변신 로그: {hl}")
copied = [m["MoveId"] for m in host.active()["Moves"]]
check(copied == guest_team[0]["MoveIds"], f"변신하면 상대 기술 4개를 그대로 가진다: {copied}")
check([t["TypeId"] for t in host.active()["Pokemon"]["Types"]] == [t["TypeId"] for t in guest.active()["Pokemon"]["Types"]],
      "변신하면 상대 타입이 된다")
host.send("USE_MOVE 4"); guest.send("USE_MOVE 1")
(hl, prompt), _ = host.turn(), guest.turn()
move4 = con.execute("SELECT name FROM PokemonMove WHERE move_id=?", (copied[3],)).fetchone()[0]
check(any(move4 in l for l in hl), f"변신 후 4번 기술({move4})을 쓴다: {hl[:3]}")
host.send("SWITCH 2"); guest.send("USE_MOVE 1")
host.turn(); guest.turn()
check([m["MoveId"] for m in host.state["Team"][0]["Moves"]] == [TRANSFORM], "교체해서 들어가면 메타몽의 기술은 변신 하나로 돌아온다")
host.send("SWITCH 1"); guest.send("USE_MOVE 1")
host.turn(); guest.turn()
check(host.active()["Pokemon"]["SpeciesId"] == DITTO and [m["MoveId"] for m in host.active()["Moves"]] == [TRANSFORM],
      "다시 나온 메타몽은 변신 하나")
host.sock.close(); guest.sock.close()

# ── 3. 내 편·상대 편 효과 표시 ──
setter, set_species = find([REFLECT, STEALTH_ROCK, SPIKES], exclude={DITTO, SMEARGLE, UNOWN})
waiter, w_species = find([SPLASH], exclude={DITTO, SMEARGLE, UNOWN, set_species})
h2, hs2 = find([], exclude={DITTO, SMEARGLE, UNOWN, set_species, w_species})
h3, _ = find([], exclude={DITTO, SMEARGLE, UNOWN, set_species, w_species, hs2})
host, guest = enter_game()
check(select(host, [member(setter, [REFLECT, STEALTH_ROCK, SPIKES]), member(h2, []), member(h3, [])]) is None, "설치 팀 선택")
check(select(guest, [member(waiter, [SPLASH]), member(h2, []), member(h3, [])]) is None, "기다리는 팀 선택")
host.turn(); guest.turn()
check(host.state["SideEffects"] == [] and host.state["OpponentSideEffects"] == [], "처음에는 양쪽 효과가 없다")

def turn(h, g="USE_MOVE 1"):
    host.send(h); guest.send(g)
    return host.turn(), guest.turn()

turn("USE_MOVE 1")    # 리플렉터
check("리플렉터 (4턴)" in host.state["SideEffects"], f"내 편에 리플렉터 (4턴): {host.state['SideEffects']}")
check("리플렉터 (4턴)" in guest.state["OpponentSideEffects"], f"상대 화면에서는 상대 편에 리플렉터: {guest.state['OpponentSideEffects']}")
check(not guest.state["SideEffects"], "리플렉터는 쓴 쪽에만 표시")
turn("USE_MOVE 2")    # 스텔스록: 상대 편에 깔린다
check("스텔스록" in guest.state["SideEffects"] and "스텔스록" in host.state["OpponentSideEffects"],
      f"스텔스록은 깔린 편(상대)에 표시: {guest.state['SideEffects']} / {host.state['OpponentSideEffects']}")
check("리플렉터 (3턴)" in host.state["SideEffects"], f"리플렉터 남은 턴이 줄어든다: {host.state['SideEffects']}")
turn("USE_MOVE 3"); turn("USE_MOVE 3")    # 압정뿌리기 2층
check("압정뿌리기 2층" in guest.state["SideEffects"], f"압정뿌리기 층수: {guest.state['SideEffects']}")
check("리플렉터 (1턴)" in host.state["SideEffects"], f"리플렉터 (1턴): {host.state['SideEffects']}")
turn("USE_MOVE 3")
check(not any(e.startswith("리플렉터") for e in host.state["SideEffects"]), f"끝난 리플렉터는 사라진다: {host.state['SideEffects']}")
check("압정뿌리기 3층" in guest.state["SideEffects"] and "스텔스록" in guest.state["SideEffects"],
      f"설치기는 계속 남는다: {guest.state['SideEffects']}")
check(host.state["FieldEffects"] == [], "날씨 등 전체 효과 줄에는 섞이지 않는다")

host.sock.close(); guest.sock.close()

# ── 4. 기술 4개인 뮤가 기술 1개인 메타몽으로 변신하면 기술도 1개가 된다 ──
MEW = 151
host, guest = enter_game()
check(select(host, [member(MEW, [TRANSFORM]), member(h2, []), member(h3, [])]) is None, "뮤 팀 선택")
check(select(guest, [{"PokemonId": DITTO, "MoveIds": [TRANSFORM]}, member(h2, []), member(h3, [])]) is None, "메타몽 팀 선택")
host.turn(); guest.turn()
turn("USE_MOVE 1")    # 뮤가 먼저 변신하고, 메타몽의 변신은 이미 변신한 상대라 실패
check([m["MoveId"] for m in host.active()["Moves"]] == [TRANSFORM], f"뮤의 기술이 메타몽처럼 변신 하나가 된다: {[m['MoveId'] for m in host.active()['Moves']]}")
e = error_of(host, "USE_MOVE 2")
check(e is not None and "슬롯" in e, f"변신 뒤에는 1번 기술만 고를 수 있다: {e}")
(hl, _), _ = turn("USE_MOVE 1")
check(any("실패" in l for l in hl), f"이미 변신했으면 변신은 실패하고 전투는 이어진다: {hl}")

p(f"결과: {'모두 통과' if not failures else str(len(failures)) + '개 실패'}")
