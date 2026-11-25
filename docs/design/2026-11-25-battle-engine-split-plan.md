# 전투 엔진 분리 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 1:1 동작을 한 글자도 바꾸지 않고, 서버 전투 규칙을 `Battle` 엔진으로 떼어내 한 편에 자리·플레이어 여러 개를 다룰 수 있게 만든다.

**Architecture:** 0단계에서 테스트를 저장소로 옮기고 무작위를 방별 `Random`으로 고정할 수 있게 한 뒤, 시드 고정 대전의 서버 메시지를 녹화한다. 1단계에서 `RoomManager.*.cs`의 전투 규칙을 `Battle/`의 `Battle` 클래스로 옮기고 1:1 진행·로비를 나눈다. 2단계에서 `BattleSide`를 편·자리(`BattleSlot`)·플레이어(`BattlePlayer`)로 나눈다. 단계마다 녹화와 비교해 메시지가 같아야 한다.

**Tech Stack:** C# / .NET 10 (`net10.0`), Microsoft.Data.Sqlite 10.0.12, 테스트는 Python 3 (소켓으로 서버에 접속해 대전)

**Spec:** [docs/design/2026-11-25-battle-engine-split.md](2026-11-25-battle-engine-split.md)

## Global Constraints

- 1:1의 프로토콜·로그 문구·메시지 순서를 바꾸지 않는다. 클라이언트 코드는 이 계획에서 수정하지 않는다.
- 새 NuGet 패키지를 추가하지 않는다. `TargetFramework`는 `net10.0` 그대로.
- 커밋·PR 작성자는 영인(YoungIn-12) 계정만. `Co-Authored-By: Claude` 등 Claude·Anthropic 표기를 넣지 않는다.
- 엔진 코드는 `roomsLock` 안에서만 호출된다. 새 잠금을 만들지 않는다.
- 기술 동작 버그를 발견해도 이 계획에서 고치지 않는다. `docs/design/2026-11-25-battle-engine-split.md` 6절 아래에 "발견한 것"으로 적는다. (예외: Task 2의 테스트 기대값 수정)
- 테스트 포트 5078·5079는 쓰지 않는다(이 PC에서 다른 프로그램이 사용).
- 코드 주석과 문서는 지금처럼 한국어로 쓴다.
- 시작 전제: `KYI(v1)`이 main에 PR로 합쳐져 있고, 작업 브랜치는 최신 main에서 만든다(이름은 영인이 정함). PR은 단계마다 하나: 0단계(Task 1~4), 1단계(Task 5~7), 2단계(Task 8~10).

## Review Focus

1. **여러 방이 동시에 전투할 때:** 방마다 `Random`이 따로라서 한 방의 전투가 다른 방의 무작위 결과에 끼어들지 않아야 한다. → Task 3의 `seed_test.py`에 "두 방 동시 진행" 확인을 둔다.
2. **재도전 후 새 전투:** 이전 전투의 날씨·필드·트릭룸이 다음 전투로 넘어오면 안 된다. → Task 4 녹화 대본 `rematch_resets_field`.
3. **전투 중 나가기·연결 끊김:** 남은 쪽에 `BATTLE_END WIN LEFT`가 그대로 가야 한다. → Task 4 녹화 대본 `leave_mid_battle`.
4. **교체 대기 중 시간 초과:** 기절 후 교체(CHOOSE_SWITCH)와 턴 도중 교체(CHOOSE_SWITCH_MIDTURN) 대기에서 시간이 지나면 자동으로 내보내고 이어서 진행해야 한다. 타이머 경로가 엔진의 새 진행 결과를 쓰는 곳이라 깨지기 쉽다. → Task 4 녹화 대본 `timeout_forced_switch`, `timeout_midturn_switch`.
5. **잘못된 명령:** 고를 수 없는 기술·쓰러진 포켓몬으로 교체·없는 메가 폼 번호에 대한 ERROR 문구가 그대로여야 한다. → Task 4 녹화 대본 `invalid_commands`.

---

## 0단계: 준비 (PR 1)

### Task 1: 테스트를 저장소로 옮기고 한 줄로 실행하기

**Files:**
- Create: `tests/run_all.py`
- Create: `tests/suites/` 아래 14개 파일 (아래 표)
- Modify: `.gitignore` (끝에 `tests/.build/`, `tests/.out/` 추가)
- Modify: `README.md` (테스트 실행 방법 절 추가)
- Add: `docs/design/2026-11-25-battle-engine-split.md`, 이 계획 파일

복사할 원본(영인 PC 임시 폴더, 지워질 수 있으니 이 Task를 먼저 한다):
`O = C:\Users\user\AppData\Local\Temp\claude\58f32cbc55c7e6dd\92c186a1-e974-4b28-8965-c8034bfdf89d\scratchpad\tests`
`N = C:\Users\user\AppData\Local\Temp\claude\58f32cbc55c7e6dd\024db965-2309-4df0-9adc-f4b8b902b831\scratchpad`

| 저장소 파일 `tests/suites/` | 원본 | 포트 |
|---|---|---|
| `attack_test.py` | `O\attack_test.py` | 5075 |
| `field_test.py` | `O\field_test.py` | 5080 |
| `power_test.py` | `O\power_test.py` | 5082 |
| `all_test.py` | `O\all_test.py` | 5084 |
| `db_stat_test.py` | `O\db_stat_test.py` | 5073 |
| `effect_test.py` | `N\tests_pp\effect_test.py` | 5083 |
| `fail_test.py` | `N\tests_pp\fail5086_test.py` | 5086 |
| `gen6_test.py` | `N\tests_pp\gen6_test.py` | 5071 |
| `status_test.py` | `N\tests_pp\status_test.py` | 5076 |
| `switch_test.py` | `N\tests_pp\switch_test.py` | 5081 |
| `strong_test.py` | `N\strong5087_test.py` | 5087 |
| `mega_test.py` | `N\mega_test.py` | 5091 |
| `mega_xy_test.py` | `N\mega_xy_test.py` | 5092 |
| `ditto_side_test.py` | `N\ditto_side_merged_test.py` | 5088 |

**Interfaces:**
- Produces: 모든 묶음은 `python <묶음>.py <Server.exe> <Pokemon.db> <결과 파일> <서버 코드 폴더>`로 실행되고, 실패가 있으면 종료 코드 1. 결과 파일의 각 확인 줄은 `PASS ` 또는 `FAIL `로 시작한다.
- Produces: `python tests/run_all.py [--only 이름,이름] [--skip-build]` → 서버를 `tests/.build/server`에 빌드하고 묶음을 차례로 실행, 묶음마다 `이름 PASS n FAIL m` 한 줄, 끝에 `전체: 통과` 또는 `전체: 실패 k개 묶음`. 하나라도 실패하면 종료 코드 1.

- [ ] **Step 1:** 표대로 파일을 복사한다. 첫 줄 주석의 사용법을 실제 파일 이름으로 고친다. 3개 인자만 받던 묶음(`strong`, `mega`, `mega_xy`, `ditto_side`)은 4번째 인자를 받아도 무시하면 된다(이미 `sys.argv[1:4]`).
- [ ] **Step 2:** "구현된 기술 번호"를 서버 코드에서 읽는 줄(`glob.glob(code_dir + "/RoomManager*.cs")`)을 모든 묶음에서 다음 함수로 바꾼다. 1단계 이후 파일이 옮겨져도 같은 코드 묶음을 읽게 하기 위해서다.

```python
def battle_code_files(code_dir):
    # 전투 규칙 코드: 옮기기 전 RoomManager*.cs, 옮긴 뒤 Battle/Battle.cs·Battle.*.cs와 Lobby·Single의 RoomManager*.cs
    pats = ["**/RoomManager*.cs", "**/Battle.cs", "**/Battle.*.cs"]
    return sorted({f for p in pats for f in glob.glob(os.path.join(code_dir, p), recursive=True)})
```

- [ ] **Step 3:** `tests/run_all.py`를 쓴다. 빌드: `dotnet build Server -o tests/.build/server -v q`. DB 경로: `Server/resources/db/Pokemon.db`. 결과 파일: `tests/.out/<이름>.txt`. 각 묶음은 `sys.executable -I`로 실행한다. 경로는 모두 `run_all.py` 위치 기준으로 계산한다(어느 폴더에서 실행해도 되게).
- [ ] **Step 4:** `python tests/run_all.py` 실행. Expected: 빌드 성공, 아래 4개 묶음만 실패하고 나머지는 모두 `FAIL 0`.
  - `effect_test` 2개, `fail_test` 1개, `power_test` 1개, `field_test`는 중력 부분에서 시간 초과로 중단
- [ ] **Step 5:** `.gitignore`·README 수정 후 커밋: `git add tests .gitignore README.md docs` / `git commit -m "테스트를 저장소로 옮기고 run_all.py 추가"`

### Task 2: 깨진 확인 항목 고치기 (테스트 쪽)

모두 서버가 아니라 테스트의 기대값·포켓몬 선택이 최근 변경(PP 도입, 중력 선택 제한, DB 기술 삭제)과 어긋난 것이다.

**Files:**
- Modify: `tests/suites/field_test.py` (중력 절, 원본 327행 근처)
- Modify: `tests/suites/power_test.py` (참기 절, 원본 257행)
- Modify: `tests/suites/fail_test.py` (171행)
- Modify: `tests/suites/effect_test.py` (289행, 375행)

- [ ] **Step 1: field_test 중력.** 중력 중에 공중날기를 고르면 이제 선택 단계에서 거절된다(SHJ #11). 공중날기를 보내 턴을 기다리던 부분을 다음 확인으로 바꾼다:
  - `error_of(guest, "USE_MOVE 1")`이 `"중력"`을 포함한 ERROR
  - 직전 `BATTLE_STATE`에서 그 기술의 `BlockedByGravity`가 `true`
  - 그 뒤 게스트는 다른 기술로 턴을 진행한다.
- [ ] **Step 2: power_test 참기.** 참기 데미지로 상대가 쓰러지면 다음 안내가 `CHOOSE_ACTION`이 아니라 `WAIT_SWITCH`다. 기대값을 `damage_of(hl, 117) == 2 * total and hp in ("CHOOSE_ACTION", "WAIT_SWITCH")`로 바꾼다.
- [ ] **Step 3: fail_test 개수.** 항상 실패 기술 목록(`FAILING`)을 8개로 줄였으므로 `check(tested >= 18, ...)`를 `check(tested == len(FAILING), ...)`로 바꾼다.
- [ ] **Step 4: effect_test 꿰뚫어보기.** 노말 기술로 잠재파워(237)가 뽑혀 타입이 바뀐다. 노말 공격기를 고를 때 타입이 바뀌는 기술 `{237, 311, 363, 449, 546}`(잠재파워·웨더볼·자연의은혜·심판의뭉치·테크노버스터)을 뺀다.
- [ ] **Step 5: effect_test 칼등치기.** 상대로 방어가 매우 높은 단단지가 뽑혀 HP가 1까지 안 내려간다. 칼등치기 상대를 `ORDER BY base_hp + base_defense` 오름차순으로 고르고, HP가 1이 될 때까지 최대 6턴 칼등치기를 반복한 뒤 `CurrentHp == 1`을 확인한다.
- [ ] **Step 6:** `python tests/run_all.py` → Expected: `전체: 통과`.
- [ ] **Step 7:** 커밋 `"PP·중력 변경에 맞춰 테스트 기대값 수정"`

### Task 3: 방마다 무작위 고정 (시드)

**Files:**
- Modify: `Server/Program.cs` (인자 파싱)
- Modify: `Server/scripts/GameRoom.cs`
- Modify: `Server/scripts/RoomManager.cs` (생성자, `CreateRoom`)
- Modify: `Server/scripts/RoomManager.{Copy,Power,Protect,Stats,Status,Switch,Timer}.cs`, `RoomManager.cs` (`Random.Shared` 31곳)
- Create: `tests/suites/seed_test.py` (포트 5093)

**Interfaces:**
- Produces: `Server.exe [포트] [--seed <정수>]`. 시드가 없으면 지금과 같다.
- Produces: `GameRoom(int id, ClientSession host, Random random)`, `public Random Random { get; }`. 방이 없어질 때까지 같은 객체를 쓴다(재도전 포함).
- Produces: `RoomManager(GameData data, int? seed = null)`. 방을 만들 때 `seed is int s ? new Random(s + roomId) : new Random()`.

- [ ] **Step 1: `seed_test.py`를 쓴다.** 같은 시드로 서버를 두 번 띄워 같은 대전(데미지 난수·급소·명중이 들어가는 공격 5턴)을 하고 양쪽 플레이어가 받은 메시지 전체를 비교한다.
  - `test_same_seed_same_messages`: 시드 42 두 번 → 메시지 완전히 같음
  - `test_different_seed_differs`: 시드 42와 43 → 한 줄 이상 다름
  - `test_two_rooms_independent`: 시드 42로 방 두 개를 만들어 번갈아 한 턴씩 진행. 두 번째 방(방 번호 1001)의 메시지가, 시드 42 서버에서 방을 하나 먼저 만들어 두기만 하고 두 번째 방만 진행했을 때와 같음
- [ ] **Step 2:** `python tests/suites/seed_test.py ...` → Expected: FAIL (`--seed` 인자를 포트로 읽다가 서버가 죽거나 메시지가 다름)
- [ ] **Step 3: 구현.**
  - `Program.cs`: 첫 인자는 포트(없으면 5050), `--seed N`이 있으면 `RoomManager`에 넘긴다.
  - `Random.Shared`를 모두 `room.Random`으로 바꾼다. 방 정보를 받지 않는 다섯 메서드(`FixedDamage`, `RollMagnitude`, `RollCritical`, `RollHitCount`, `ConfusionDamage`)는 첫 매개변수로 `GameRoom room`을 받게 한다.
- [ ] **Step 4:** `seed_test` 통과, `python tests/run_all.py` → `전체: 통과`
- [ ] **Step 5:** 커밋 `"서버 --seed 인자: 방마다 무작위 고정"`

### Task 4: 녹화 비교 도구와 기준 녹화

**Files:**
- Create: `tests/replay/replay.py`, `tests/replay/scenarios.py`
- Create: `tests/replay/baseline/<대본>.host.txt`, `<대본>.guest.txt` (녹화 결과, 커밋한다)
- Modify: `tests/run_all.py` (`--replay` 옵션: 묶음 실행 뒤 `replay.py check` 실행)

**Interfaces:**
- Produces: `python tests/replay/replay.py record|check <Server.exe> [--only 대본]`. 대본마다 새 서버를 `--seed 7`, 포트 5094로 띄운다. `record`는 기준 파일을 덮어쓰고, `check`는 다르면 대본 이름과 처음 다른 줄(번호·기준·지금)을 출력하고 종료 코드 1.
- Produces: 대본 형식 (`scenarios.py`)

```python
# 대본 하나. 각 플레이어는 입력을 요구하는 안내(CHOOSE_ACTION, CHOOSE_SWITCH, CHOOSE_SWITCH_MIDTURN)나
# 결과 화면(BATTLE_END)을 받을 때마다 자기 commands에서 다음 명령을 꺼내 보낸다.
# "WAIT" = 보내지 않고 서버의 시간 초과 자동 선택을 기다림, "EXPECT_ERROR <명령>" = 보내고 ERROR 한 줄을 받음,
# "LEAVE"/"DISCONNECT" = 나가기/소켓 끊기. 명령이 떨어지면 그 플레이어의 녹화를 끝낸다.
Scenario = dict(name=str, host_team=list, guest_team=list, host=list[str], guest=list[str])
```

- 녹화 파일은 그 플레이어가 받은 모든 줄을 받은 순서대로 담는다(닉네임은 고정: `h`, `g`).
- 시간 초과 대본 3개가 각각 61초 이상 기다리므로 `check` 한 번에 4~5분 걸린다. 빨리 확인할 때는 `--only`로 나머지만 돌린다.

필요한 대본(포켓몬·기술은 서버가 `SELECTION_ACCEPTED`로 받아 주는 조합으로 고른다):

| 대본 | 들어가야 하는 것 |
|---|---|
| `mega_xy` | 리자몽 메가 Y 선택, 상대가 더 빠름, 교체 후 메가 폼 유지 |
| `uturn_baton` | 유턴·배턴터치 턴 도중 교체, 따라가때리기 |
| `two_turn_moves` | 솔라빔·공중날기, 반동으로 쉬는 턴(파괴광선), 난동 기술 |
| `weather_field` | 비바라기+번개, 그래스필드, 트릭룸, 중력(선택 거절 포함) |
| `hazards` | 스텔스록·압정뿌리기·독압정, 교체로 맞기, 고속스핀 |
| `transform_struggle` | 메타몽 변신, PP를 다 써서 발버둥 |
| `faint_and_replace` | 기절 → CHOOSE_SWITCH / WAIT_SWITCH → 끝까지 진행해 BATTLE_END WIN/LOSE |
| `rematch_resets_field` | 비 상태에서 항복 → 양쪽 REMATCH → 두 번째 전투에 날씨 없음 |
| `leave_mid_battle` | 전투 중 게스트 DISCONNECT → 호스트 `BATTLE_END WIN LEFT` |
| `invalid_commands` | 사슬묶기된 기술, 쓰러진 포켓몬으로 SWITCH, 없는 메가 폼 번호, 메가진화 두 번째 시도 (모두 `EXPECT_ERROR`) |
| `timeout_action` | 한쪽이 `WAIT` → 기술 자동 선택 |
| `timeout_forced_switch` | 기절 후 교체에서 `WAIT` |
| `timeout_midturn_switch` | 유턴 뒤 교체에서 `WAIT` |

- [ ] **Step 1:** `replay.py`와 `scenarios.py`를 쓴다.
- [ ] **Step 2:** `replay.py record` 실행 → `baseline/`에 26개 파일 생성. 각 파일이 `BATTLE_END`(또는 의도한 끝)까지 있는지 눈으로 확인한다.
- [ ] **Step 3:** `replay.py check`를 두 번 실행 → Expected: 두 번 모두 차이 없음(결정적임을 확인)
- [ ] **Step 4:** 민감도 확인: `Server/scripts/RoomManager.Status.cs`의 로그 문구 하나를 임시로 바꾸고 빌드 → `check`가 그 대본 이름과 줄을 보고하며 실패하는지 확인 → 되돌린다.
- [ ] **Step 5:** `python tests/run_all.py --replay` → `전체: 통과`. 커밋 `"녹화 비교 도구와 1:1 기준 녹화"`. **0단계 PR을 main에 올린다.**

---

## 1단계: 옮기기 (PR 2)

시작 전에 SHJ 팀원의 서버 전투 작업이 main에 합쳐졌는지 영인이 확인한다. 작업 브랜치에 main을 받아 온 뒤 `run_all.py --replay`가 통과하는 상태에서 시작한다.

### Task 5: 파일 이동만 하는 커밋

**Files (git mv, 내용 변경 없음):**
- `Server/scripts/RoomManager.{Attack,Copy,Effect,Field,Heal,Power,Protect,Stats,Status,Switch}.cs` → `Server/scripts/Battle/Battle.{같은 이름}.cs`
- `Server/scripts/BattleSide.cs`, `BattlePokemon.cs`, `BattleMoveSlot.cs` → `Server/scripts/Battle/`
- `Server/scripts/RoomManager.cs`, `GameRoom.cs` → `Server/scripts/Lobby/`
- `Server/scripts/RoomManager.Timer.cs` → `Server/scripts/Single/RoomManager.Timer.cs`

- [ ] **Step 1:** `git mv`로 옮긴다. 클래스 이름은 아직 `RoomManager` 그대로다(partial class는 폴더가 달라도 합쳐진다).
- [ ] **Step 2:** `python tests/run_all.py --replay` → `전체: 통과`
- [ ] **Step 3:** `git status`에 이동(renamed)만 있는지 확인하고 커밋 `"전투 코드 파일 위치 이동 (내용 변경 없음)"`

### Task 6: `Battle` 클래스와 1:1 진행 분리

**Files:**
- Create: `Server/scripts/Battle/Battle.cs`, `Server/scripts/Battle/Battle.Views.cs`, `Server/scripts/Battle/TurnProgress.cs`
- Create: `Server/scripts/Single/RoomManager.Single.cs`
- Modify: `Battle/Battle.*.cs` 10개 (`partial class RoomManager` → `partial class Battle`)
- Modify: `Lobby/RoomManager.cs`, `Lobby/GameRoom.cs`, `Single/RoomManager.Timer.cs`

**Interfaces:**
- Produces:

```csharp
enum TurnProgress { NextTurn, AwaitingMidTurnSwitch, Over }   // 엔진이 다음에 기다리는 것
enum BattleOutcome { HostWins, GuestWins, Draw }

sealed partial class Battle
{
    public Battle(GameData data, Random random, BattleSide host, BattleSide guest);
    public BattleSide HostSide { get; }
    public BattleSide GuestSide { get; }
    public Random Random { get; }
    public BattleOutcome? Outcome { get; }          // Over를 돌려준 뒤에만 값이 있다
    // GameRoom에서 옮겨 온 전투 상태 (이름·타입 그대로): TrickRoomTurns, Weather, WeatherTurns, Terrain, TerrainTurns,
    // GravityTurns, WonderRoomTurns, MudSportTurns, WaterSportTurns, FairyLockTurns, IonDeluge, TurnQueue,
    // TurnNumber, LastMoveIdThisTurn, LastExecutedMove, EchoedVoiceTurn, EchoedVoiceCount

    public void BeginTurn();                         // 지금 StartTurn 앞부분: 난동·2턴·반동이면 행동을 정해 둔다
    public bool AllChosen { get; }                   // 양쪽 HasChosen
    public TurnProgress ResolveTurn();               // 지금 TryResolveTurn 본문(선택 확인 제외) + ContinueTurn
    public TurnProgress CompleteMidTurnSwitch(BattleSide side, int index);
    public void SendInReplacement(BattleSide side, int index);
    public TurnProgress CheckEnd();                  // 한쪽이 전멸이면 Outcome을 정하고 Over, 아니면 NextTurn
    public void SendBattleState(BattleSide? megaEvolved = null);
    public void Log(BattleSide actor, string mine, string theirs);
    // 모드가 선택을 검증할 때 쓰는 규칙
    public string? MoveBlockedBy(BattleSide side, BattleSide other, int slot);
    public bool MustStruggle(BattleSide side, BattleSide other);
    public IReadOnlyList<PokemonInfo> GetMegaForms(BattleSide side);
    public bool IsTrapped(BattleSide side, BattleSide other);
    public BattleSide GetOther(BattleSide side);
    public static string WithJosa(string word, string afterFinal, string afterVowel);
    public const int StruggleMoveId;                 // 값은 지금 그대로
}
```

- Produces: `GameRoom`에는 방 정보만 남는다: `Id`, `Host`, `Guest`, `HostTeam`, `GuestTeam`, `Random`, `Battle? Battle`, `BattleOver`, `TeamDeadline`, `ActionDeadline`, `HostRematch`, `GuestRematch`, `ResetBattle()`(이제 `Battle = null`도 함).
- Produces: `Single/RoomManager.Single.cs`에 `UseMove`, `SwitchPokemon`, `Surrender`, `RequestRematch`, `TryGetBattle(ClientSession, out GameRoom, out Battle, out BattleSide me, out BattleSide other)`, `SendPrompts(GameRoom)`, 그리고:

```csharp
// 엔진이 입력을 기다릴 때까지 진행시키고, 기다리는 입력을 안내한다. 지금 StartTurn·ContinueTurn·EndBattleIfOver가 하던 일.
private void Proceed(GameRoom room, TurnProgress progress)
{
    // Over: room.BattleOver = true; Outcome에 따라 지금과 같은 BATTLE_END WIN/LOSE/DRAW와 콘솔 로그
    // AwaitingMidTurnSwitch: SendPrompts, ActionDeadline 설정, 끝
    // NextTurn: battle.BeginTurn(), SendPrompts, ActionDeadline 설정,
    //           battle.AllChosen이면 progress = battle.ResolveTurn()으로 반복, 아니면 끝
}
```

- [ ] **Step 1:** `Battle/` 10개 파일의 클래스 이름을 `Battle`로 바꾸고, `GameRoom room` 매개변수를 모두 없앤다(`room.X` → `X`, `GetOther(room, s)` → `GetOther(s)`). 플레이어에게 직접 보내던 곳(`x.Player.QueueMessage`, 9곳)은 모두 `private void Send(BattleSide side, string message)`를 거치게 한다. 이 단계의 `Send`는 `side.Player.QueueMessage(message)` 한 줄이다.
- [ ] **Step 2:** `Lobby/RoomManager.cs`에서 전투 규칙(`TryResolveTurn`, `ContinueTurn`, `ChangeActive`, `CalculateDamage`, `DescribeMove`, `WithJosa`, `Log`, `ApplyChosenMegaEvolution`, `GetMegaForms`, `GetOther`)은 `Battle/Battle.cs`로, 화면 정보(`SendBattleState`, `GetPokemon`, `GetMove(BattleMoveSlot)`)는 `Battle/Battle.Views.cs`로, 1:1 행동 처리는 `Single/RoomManager.Single.cs`로 옮긴다. 포켓몬 목록 만들 때 쓰는 `GetType`·`GetTypes`·`GetMove(PokemonMoveInfo)`는 `RoomManager`와 `Battle`이 같이 쓰므로 `Battle/Battle.Views.cs`에 `internal static` 메서드로 두고 `GameData`를 인자로 받는다.
- [ ] **Step 3:** 타이머(`ActionTimeout`)를 새 경계에 맞춘다: 자동 교체는 `battle.SendInReplacement` 뒤 `Proceed(room, battle.CheckEnd())`, 턴 도중 교체는 `Proceed(room, battle.CompleteMidTurnSwitch(...))`, 자동 기술 선택 뒤 `battle.AllChosen`이면 `Proceed(room, battle.ResolveTurn())`. `TeamTimeout`은 `room.Battle`이 없을 때도 동작해야 한다.
- [ ] **Step 4:** `SelectTeam` 끝에서 `room.Battle = new Battle(data, room.Random, hostSide, guestSide)`, `battle.SendBattleState()`, `Proceed(room, TurnProgress.NextTurn)`.
- [ ] **Step 5:** 빌드 경고 0, `python tests/run_all.py --replay` → `전체: 통과`. 녹화와 다르면 메시지 순서가 바뀐 곳(대개 `SendBattleState`와 `BATTLE_END`의 순서)을 고친다. 녹화 파일은 고치지 않는다.
- [ ] **Step 6:** 커밋 `"전투 규칙을 Battle 클래스로 분리하고 1:1 진행을 Single로 이동"`

### Task 7: 문서와 1단계 PR

**Files:**
- Modify: `README.md` (서버 파일 위치 안내: 기술 효과는 `Server/scripts/Battle/`, 1:1 진행은 `Single/`, 로비는 `Lobby/`)

- [ ] **Step 1:** README에서 `RoomManager.Status.cs`처럼 옛 경로를 가리키는 링크·문장을 새 경로로 고친다. `grep -n "RoomManager\." README.md`에 남은 옛 경로가 없어야 한다.
- [ ] **Step 2:** `python tests/run_all.py --replay` → `전체: 통과`. 커밋 `"README 파일 위치 안내 갱신"`.
- [ ] **Step 3:** 1단계 PR을 main에 올린다. 영인이 팀원에게 새 위치를 알린다.

---

## 2단계: 편·자리·플레이어 (PR 3)

### Task 8: `BattlePlayer`

**Files:**
- Create: `Server/scripts/Battle/BattlePlayer.cs`
- Modify: `Battle/BattleSide.cs`, `Battle/Battle.cs`, `Battle/Battle.Views.cs`, `Single/RoomManager.Single.cs`

**Interfaces:**
- Produces:

```csharp
sealed class BattlePlayer
{
    public BattlePlayer(ClientSession? session, int[] teamIndices);
    public ClientSession? Session { get; }            // AI면 null
    public IReadOnlyList<int> TeamIndices { get; }    // 편의 Team에서 이 플레이어가 맡은 번호
    public bool HasMegaEvolved { get; set; }          // 2:2 규칙: 메가진화는 플레이어마다 한 번
}
```

- Produces: `BattleSide.Players`(`BattlePlayer[]`). `BattleSide.Player`와 `BattleSide.HasMegaEvolved`는 없앤다.
- Produces: `Battle.Send(BattleSide side, string message)`는 `side.Players`에서 `Session`이 있는 모두에게 보낸다.

- [ ] **Step 1:** 바꾸고, 1:1은 편마다 `new BattlePlayer(session, [0, 1, 2])` 하나로 만든다. 메가진화 사용 여부는 그 편의 유일한 플레이어에 기록한다(Task 9에서 자리 주인으로 바뀜).
- [ ] **Step 2:** `python tests/run_all.py --replay` → `전체: 통과`. 커밋 `"플레이어를 BattlePlayer로 분리"`

### Task 9: `BattleSlot`과 엔진 매개변수 바꾸기

**Files:**
- Create: `Server/scripts/Battle/BattleSlot.cs`
- Modify: `Battle/BattleSide.cs`, `Battle/Battle*.cs` 전부, `Single/RoomManager.Single.cs`, `Single/RoomManager.Timer.cs`

**Interfaces:**
- Produces:

```csharp
sealed class BattleSlot
{
    public BattleSlot(BattleSide side, int index, BattlePlayer owner, int active);
    public BattleSide Side { get; }
    public int Index { get; }                 // 편 안의 자리 번호. 1:1은 0
    public BattlePlayer Owner { get; }
    public int Active { get; set; }           // 출전 중인 Side.Team 번호
    public BattlePokemon Current => Side.Team[Active];
    // BattleSide에서 옮겨 오는 것 (이름·타입 그대로): ChosenMove, ChosenMegaForm, ChosenSwitch, HasChosen, Struggling,
    // TurnActor, SwitchingMidTurn, BatonPassing, MustSwitch, WishTurns, WishHeal,
    // FutureAttackTurns, FutureAttackUser, FutureAttackMove, HealingWish
}
```

- Produces: `BattleSide.Slots`(`BattleSlot[]`). `BattleSide`에 남는 것: `Team`, `Players`, `Slots`, `FaintedThisTurn`, `FaintedLastTurn`, 편 전체 효과(리플렉터~끈적끈적네트, 가드류).
- Produces: `Battle.OpposingSide(BattleSide side)`, `Battle.DefaultTarget(BattleSlot user)`(상대 편의 `Slots[0]`). `GetOther`는 없앤다.
- Produces: 엔진 메서드의 `(BattleSide attacker, BattleSide defender)`는 `(BattleSlot user, BattleSlot target)`으로, 출전 포켓몬이나 자리 상태를 쓰던 `BattleSide side`는 `BattleSlot slot`으로 바꾼다. 편 전체 효과만 쓰는 메서드는 `BattleSide` 그대로다. `TurnQueue`는 `List<BattleSlot>`이다. `Log`·`Send`·`SendBattleState`의 기준은 편이므로 `BattleSide`를 받는다.
- Produces: `MoveBlockedBy(BattleSlot slot, int moveSlot)`, `MustStruggle(BattleSlot slot)`, `GetMegaForms(BattleSlot slot)`(사용 여부는 `slot.Owner.HasMegaEvolved`), `IsTrapped(BattleSlot slot)`, `TurnProgress CompleteMidTurnSwitch(BattleSlot slot, int index)`, `void SendInReplacement(BattleSlot slot, int index)`. 반환형은 Task 6과 같다.
- Produces: `TryGetBattle(..., out BattleSlot me, out BattleSlot other)`.

- [ ] **Step 1:** `BattleSlot`을 만들고 위 필드를 옮긴다. 1:1은 편마다 자리 하나 `new BattleSlot(side, 0, side.Players[0], 0)`.
- [ ] **Step 2:** 컴파일러 오류를 따라 엔진 매개변수를 바꾼다. "상대 포켓몬"을 전부 가리키던 곳(교체 시 헤롱헤롱 해제, 소란, 턴 끝 처리 순서 등)은 `OpposingSide(side).Slots` 전체를 돈다. 1:1에서는 결과가 같다.
- [ ] **Step 3:** 빌드 경고 0, `python tests/run_all.py --replay` → `전체: 통과`
- [ ] **Step 4:** 커밋 `"출전 자리를 BattleSlot으로 분리"`

### Task 10: 2단계 마무리

**Files:**
- Modify: `README.md` (서버 구조 절에 편·자리·플레이어 설명 3줄)
- Modify: `docs/design/2026-11-25-battle-engine-split.md` (6절 "발견한 것"이 있으면 정리)

- [ ] **Step 1:** 문서 수정 후 `python tests/run_all.py --replay` → `전체: 통과`
- [ ] **Step 2:** 커밋 `"엔진 분리 2단계 문서"` 후 2단계 PR을 main에 올린다.
