# 포켓몬 도감 DB

`Pokemon.db`는 SQLite 데이터베이스이며, `Pokemon` 테이블에 전국도감 1~649번을 각 1행씩 저장합니다. 스키마는 `schema.sql`에 있습니다.

`Type` 테이블은 5세대의 17개 타입 ID(`type_id`), 한국어 이름(`name`), 터미널 출력용 RGB 색상(`color_rgb`, `#RRGGBB`)을 저장합니다. `Pokemon.type1_id`, `Pokemon.type2_id`, `PokemonMove.type_id`는 모두 `Type.type_id`를 참조합니다. 단일 타입 포켓몬의 `type2_id`만 `NULL`입니다.

`TypeEffectiveness` 테이블은 공격 타입 ID(`attack_type_id`), 방어 타입 ID(`defend_type_id`), 배율(`multiplier`)을 저장합니다. 공격·방어 타입 ID의 조합이 기본 키이며, 두 ID 모두 `Type`을 참조합니다. 17개 타입의 모든 조합 289개를 저장하므로 1배 상성도 행으로 존재합니다. 5세대까지의 상성을 적용해 고스트·악 기술은 강철에 0.5배입니다.

`PokemonMove` 테이블은 기술 순번(`move_id`)을 기본 키로 사용하며, 이름(`name`), 타입 ID(`type_id`), 분류(`category`), 위력(`power`), 명중률(`accuracy`), PP(`pp`)를 저장합니다. 위력과 명중률은 값이 없는 기술을 위해 `NULL`을 허용하고, 나머지 열은 `NOT NULL`입니다.

- 기술 1~559번을 각 1행씩 저장합니다. 이름과 타입은 한국어이며, 분류는 `물리`·`특수`·`변화`입니다.
- 위력이나 명중률이 원본에 없으면 `NULL`입니다. 원본의 `0`은 별도 값으로 그대로 저장합니다.
- 560번 이후 기술은 수록하지 않습니다.

### 연속기 여부

`PokemonMove.is_multi_hit`는 한 턴에 여러 번 타격하는 연속기 여부입니다. SQLite의 `INTEGER NOT NULL DEFAULT 0` 열이며 `0`(아님), `1`(연속기)만 허용합니다.

- 기존 DB와 같은 PokéAPI 커밋 `bc92d3b6029ef1abe9e7ad424c400b338f3c11fe`의 [move_meta.csv](https://github.com/PokeAPI/pokeapi/blob/bc92d3b6029ef1abe9e7ad424c400b338f3c11fe/data/v2/csv/move_meta.csv)에서 `max_hits > 1`인 기술을 기준으로 분류했습니다.
- 기술 1~559번 중 21개가 연속기입니다. 두번차기, 연속뺨치기, 트리플킥, 집단구타 등이 포함됩니다.
- 연속기 여부만 나타내며 실제 타격 횟수, 횟수 확률, 타격별 명중·위력 규칙은 별도로 구현해야 합니다. 여러 턴 동안 지속되는 기술 여부와는 다릅니다.
- 서버 시작 시 `GameData.Load()`에서 `PokemonMoveInfo.IsMultiHit`로 읽습니다. DB는 실행 중 수정하지 않으며, 변경된 데이터를 사용하려면 서버를 다시 빌드·시작해야 합니다.
- 이번 변경은 기존 DB를 직접 수정한 것입니다. DB 수정·재구축용 파이썬 코드나 서버 실행 시 마이그레이션은 추가하지 않았습니다.

```sql
SELECT move_id, name, is_multi_hit
FROM PokemonMove
WHERE is_multi_hit = 1
ORDER BY move_id;
```

- `pokemon_id`: 전국도감 번호. 폼별 내부 ID가 아닌 종의 `species_id`를 사용합니다.
- `name`: 한국어 이름. `type1_id`, `type2_id`의 순서는 원본의 타입 슬롯 1, 2를 따릅니다.
- 6개 능력치는 레벨별 실능력치가 아닌 종족값입니다.
- 각 종은 PokéAPI의 기본 폼(`is_default=1`)을 사용합니다. 리전 폼, 메가진화, 기타 대체 폼은 별도 행으로 넣지 않습니다.
- 650번 이후 포켓몬은 수록하지 않습니다. 페어리로 변경된 포켓몬 22종과 기술 3개의 타입은 원본의 과거 변경 기록을 사용해 5세대 값으로 복원했습니다. 종족값과 기술의 위력·명중률·PP는 원본 버전의 현재 값이므로 5세대 당시 값과 다를 수 있습니다.

## 상태이상과 기술별 적용 효과

`StatusCondition`은 상태이상 정의, `MoveStatusEffect`는 기술과 상태이상의 연결을 저장합니다. 기존 DB를 직접 수정했으며, 파이썬 DB 수정·재구축 코드나 실행 시 마이그레이션은 추가하지 않았습니다.

### StatusCondition

| 열 | 의미 |
| --- | --- |
| `status_id` | 상태이상 기본 키 |
| `code` | 고유 식별자 (`paralysis`, `sleep`, `freeze`, `burn`, `poison`, `confusion`, `infatuation`, `bad-poison`) |
| `name` | 한국어 이름 |

마비·잠듦·얼음·화상·독·혼란·헤롱헤롱·맹독의 8종입니다. ID 1~7은 PokéAPI의 상태 ID를 따르고, 맹독은 일반 독과 구분하기 위해 프로젝트 전용 ID `1001`을 사용합니다.

### MoveStatusEffect

| 열 | 의미 |
| --- | --- |
| `move_id` | `PokemonMove.move_id` 외래 키 |
| `status_id` | `StatusCondition.status_id` 외래 키 |
| `chance` | 적용 확률(%), `REAL`, 0 초과 100 이하 |
| `target` | `opponent`(상대) 또는 `self`(자신) |
| `timing` | `on_hit`(기술 적중 후), `on_use`(사용 성공 시), `after_sequence`(연속 턴 기술 종료 후), `end_next_turn`(다음 턴 종료 시) |
| `effect_group` | `0`은 개별 효과. 같은 기술·대상·시점의 같은 양수 그룹은 그중 최대 하나를 적용하는 추첨 그룹 |

기본 키는 `(move_id, status_id, target, timing)`입니다. 상태이상 없는 기술은 연결 행이 없습니다. 현재 85개 기술에 87개 효과가 등록되어 있습니다.

- 기존 도감과 같은 PokéAPI 커밋의 [move_meta.csv](https://github.com/PokeAPI/pokeapi/blob/bc92d3b6029ef1abe9e7ad424c400b338f3c11fe/data/v2/csv/move_meta.csv), `moves.csv`, `move_effect_prose.csv`를 사용했습니다. 메타데이터의 상태 ID 1~7을 가져오고 특수 적용 시점을 보완했습니다. 기술의 기본 수치처럼 이 확률도 해당 원본의 값이며, 모든 수치를 5세대 당시 값으로 복원한 것은 아닙니다.
- 변화 기술의 원본 `ailment_chance = 0`은 적용 확률 0%가 아닙니다. 상태 부여 자체가 기술의 주 효과인 경우 100%로 저장했습니다. 별도의 명중 판정과 타입·특성 면역 등은 전투 로직에서 처리해야 합니다.
- 10만볼트는 적중 후 마비 10%, 전기자석파는 적중 후 마비 100%입니다. 확률을 기술 명중률과 미리 곱하지 않았습니다.
- 맹독(92)과 맹독엄니(305)는 일반 독 대신 맹독으로 연결했습니다.
- 트라이어택(161)은 마비·얼음·화상 각 `20/3%`, `effect_group = 1`입니다. 합계 20% 확률로 셋 중 하나만 적용하며, 각 행을 독립 판정하거나 여러 상태를 동시에 적용하면 안 됩니다. 그룹에서 선택된 상태가 면역이라고 다른 상태를 다시 추첨하는 규칙도 아닙니다.
- 잠자기(156)는 자신에게 잠듦 100% (`on_use`), 난동부리기(37)·꽃잎댄스(80)·역린(200)은 자신에게 혼란 100% (`after_sequence`), 하품(281)은 상대에게 잠듦 100% (`end_next_turn`)입니다.
- 현재 범위는 위 8종입니다. 풀죽음·조임·씨뿌리기 같은 다른 효과, 사이코시프트의 기존 상태 이전, 내던지기·비밀의힘 같은 조건부 효과는 이 연결만으로 구현되지 않습니다. 지속 턴 수와 구체적인 상태 동작도 별도 전투 로직에서 처리합니다.

서버 시작 시 `GameData.StatusConditionsById`와 `GameData.MoveStatusEffectsByMoveId`에 읽기 전용으로 적재합니다. 효과가 없는 기술도 후자에서 빈 목록으로 조회할 수 있습니다. 실제 전투에서 걸린 상태는 DB에 쓰지 않고 포켓몬 전투 객체에 보관합니다.

```csharp
foreach (var effect in data.MoveStatusEffectsByMoveId[85])
{
    var status = data.StatusConditionsById[effect.StatusId];
    // status.Name: 마비, effect.Chance: 10
    // target, timing, effect_group을 확인한 뒤 전투 로직에서 판정한다.
}
```

```sql
SELECT m.name AS move, s.name AS status, e.chance, e.target, e.timing, e.effect_group
FROM MoveStatusEffect AS e
JOIN PokemonMove AS m USING (move_id)
JOIN StatusCondition AS s USING (status_id)
WHERE e.move_id IN (85, 92, 156, 161, 281, 305)
ORDER BY e.move_id, e.status_id;
```

## 습득 가능 기술 (레벨업·교배·비전머신)

`PokemonLearnset`은 전국도감 1~649번 기본 폼의 **블랙2·화이트2 레벨업 기술 + 1~5세대 교배기·비전머신 기술**을 합쳐 13,467개 포켓몬·기술 조합을 저장합니다. 기존 레벨업 9,082개에 중복을 제외한 4,385개 조합이 추가되었습니다.

- `pokemon_id`: `Pokemon.pokemon_id` 외래 키.
- `move_id`: `PokemonMove.move_id` 외래 키. 기술 1~559번만 참조합니다.

기본 키는 `(pokemon_id, move_id)`입니다. 습득 레벨을 저장하지 않으며, 어느 레벨에서든 한 번이라도 배우는 기술이면 한 행으로 기록합니다. 여러 레벨에서 반복해서 배우는 기술도 한 행만 남깁니다.

교배기 2,933개 조합과 비전머신 기술 1,672개 조합은 1~5세대 게임에서 해당 기본 폼이 직접 배우는 목록의 합집합입니다. 비전머신은 습득 방법 `machine`인 행을 같은 게임 버전의 머신·아이템 목록과 연결하여 `hm01`~`hm08`만 추립니다. 일반 기술머신·기술 가르침·전기구슬·폼 체인지 경로는 포함하지 않습니다. 진화 전 단계의 목록은 자동으로 전파하지 않으며, 레벨업은 기존 블랙2·화이트2 기준을 유지합니다. 습득 방법·레벨·버전·순서 열은 저장하지 않고, 여러 경로에서 배우는 기술도 한 행만 저장합니다. 서버 시작 시 이 목록을 메모리에 적재합니다. 클라이언트는 선택한 포켓몬의 습득 기술만 표시하고 선택을 제한하며, 서버에서도 최종 선택을 검증합니다. 현재 배틀은 서로 다른 기술 4개가 필요하므로 습득 가능 기술이 4개 미만인 포켓몬은 다시 선택하도록 안내합니다. 기술 목록은 타입 색상·타입·기술 이름(터미널 8칸)·위력(3칸)·명중률(3칸)을 표시하며, 없는 위력과 명중률은 `-`로 표시합니다.

기존 DB에 적용하려면 프로젝트 루트에서 실행합니다.

```powershell
python Pokemon_server/scripts/add_pokemon_learnsets.py
```

스크립트는 기존 목록을 보존하면서 누락된 교배기·비전머신 조합을 추가합니다. 기존 레벨 열이 있는 테이블도 포켓몬·기술 연결 테이블로 바꾸고 중복 조합을 합칩니다. 기존 목록에 원본 합집합에 없는 조합이 있으면 덮어쓰지 않고 오류로 중단합니다. 기존 포켓몬·기술·타입·상성 행은 유지하며, 반복 실행 결과가 같고 오류 시 트랜잭션 전체를 되돌립니다.

## 출처와 재현

출처: [PokéAPI CSV 데이터](https://github.com/PokeAPI/pokeapi/tree/bc92d3b6029ef1abe9e7ad424c400b338f3c11fe/data/v2/csv), [PokéAPI 문서](https://pokeapi.co/docs/v2).

- 기존 도감·기술 데이터 다운로드 날짜: 2026-10-01
- 레벨업 기술 습득 데이터 다운로드 날짜: 2026-10-02
- 교배기·비전머신 데이터 다운로드 날짜: 2026-10-08
- 원본 커밋: `bc92d3b6029ef1abe9e7ad424c400b338f3c11fe`
- `source/`에 포켓몬과 기술 생성에 사용한 CSV 원본을 보관했습니다. `pokemon_levelup_moves_black2_white2.csv`는 같은 커밋의 `pokemon_moves.csv`에서 블랙2·화이트2, 기본 폼 1~649번, 기술 1~559번의 레벨업 경로를 추린 뒤 습득 레벨을 제거하고 포켓몬·기술 조합을 중복 없이 남긴 스냅샷입니다. 필터·원본/저장 파일 SHA-256은 `source/learnset_source.json`에 기록했습니다. 재생성 시 네트워크와 외부 Python 패키지가 필요하지 않습니다.
- `pokemon_egg_moves_gen1_5.csv`, `pokemon_hm_moves_gen1_5.csv`는 같은 커밋에서 추린 교배기·비전머신 스냅샷입니다. 필터, 원본·스냅샷 SHA-256, 경로별 개수는 `source/egg_hm_learnset_source.json`에 기록했습니다.
- 교배기·비전머신 스냅샷을 다시 추출하려면 위 커밋의 `pokemon_moves.csv`, `machines.csv`, `items.csv`, `pokemon_move_methods.csv`를 한 폴더에 다운로드한 뒤 `python Pokemon_server/scripts/snapshot_egg_hm_learnsets.py <원본_CSV_폴더>`를 실행합니다. 일반 DB 갱신·재생성에는 이 다운로드가 필요하지 않습니다.
- 프로젝트 루트에서 `python Pokemon_server/DB/build_db.py`를 실행합니다. 기존 DB가 있으면 덮어쓰지 않으므로, 재생성하려면 먼저 기존 파일을 다른 이름으로 보관해야 합니다.

생성 과정에서 포켓몬 1~649번과 기술 1~559번의 연속성·중복·누락, 필수 데이터, 종족값 범위, 타입 외래 키, 289개 상성, 649마리의 습득 가능 기술 목록·포켓몬/기술 조합 중복, 저장 결과와 원본 일치, SQLite 무결성을 검사합니다.

## 조회 예시

```sql
SELECT * FROM Pokemon WHERE pokemon_id = 25;
SELECT COUNT(*), MIN(pokemon_id), MAX(pokemon_id) FROM Pokemon;
SELECT * FROM PokemonMove WHERE move_id = 1;
SELECT COUNT(*), MIN(move_id), MAX(move_id) FROM PokemonMove;
SELECT t1.name AS type1, t2.name AS type2
FROM Pokemon AS p
JOIN Type AS t1 ON t1.type_id = p.type1_id
LEFT JOIN Type AS t2 ON t2.type_id = p.type2_id
WHERE p.pokemon_id = 25;
SELECT e.multiplier
FROM TypeEffectiveness AS e
JOIN Type AS attack ON attack.type_id = e.attack_type_id
JOIN Type AS defend ON defend.type_id = e.defend_type_id
WHERE attack.name = '불꽃' AND defend.name = '풀';
SELECT type_id, name, color_rgb FROM Type ORDER BY type_id;
```


### 피카츄가 배울 수 있는 기술 목록

```sql
SELECT m.move_id, m.name, t.name AS type
FROM PokemonLearnset AS l
JOIN PokemonMove AS m ON m.move_id = l.move_id
JOIN Type AS t ON t.type_id = m.type_id
WHERE l.pokemon_id = 25
ORDER BY m.move_id;
```

### 특정 기술을 배울 수 있는지 확인

```sql
SELECT EXISTS (
    SELECT 1 FROM PokemonLearnset
    WHERE pokemon_id = 25 AND move_id = 85
) AS can_learn;
```

행이 있으면 해당 포켓몬이 블랙2·화이트2 레벨업 또는 1~5세대 교배·비전머신 경로로 배우는 기술입니다. 실제 습득 방법·레벨과 무관하게 판단합니다.

서버에서 사용할 연결 문자열 예: `Data Source=DB/Pokemon.db;Mode=ReadOnly`.
상대 경로는 실행 시 작업 디렉터리 기준이므로, 서버 실행 위치에 맞춰 절대 경로를 지정하거나 DB 경로를 구성해야 합니다.
현재 서버는 시작 시 포켓몬·기술·타입·상성·습득 기술 데이터를 읽기 전용 메모리 사전에 적재하고, 클라이언트 요청 처리에는 적재한 데이터를 사용합니다. DB 내용을 바꾼 뒤에는 서버를 다시 시작해야 합니다.

## 3마리 팀과 교체

클라이언트는 포켓몬 3마리와 각 포켓몬의 습득 가능 기술 4개를 선택해 전송합니다. 첫 번째 포켓몬이 먼저 출전하며, 같은 종을 여러 슬롯에 선택할 수도 있습니다. 다음 포켓몬 선택에서 `b`를 입력하면 이전 포켓몬의 마지막 기술 선택으로 돌아갑니다.

전투 중 기술은 `1`~`4`, 교체는 `S`를 누른 뒤 팀 번호 `1`~`3`으로 선택합니다. 자발적 교체 선택은 `Esc`로 취소할 수 있습니다. 교체 목록에는 각 포켓몬의 현재/최대 HP, 기절 여부, 현재 출전 표시(`*`)가 표시됩니다.

자발적 교체는 한 턴을 사용하며 상대의 공격보다 먼저 처리합니다. 쓰러진 포켓몬의 강제 교체는 상대에게 추가 공격을 허용하지 않습니다. 벤치 포켓몬의 체력은 유지되며 현재 출전 중이거나 쓰러진 포켓몬으로 교체할 수 없습니다. 상대 피카츄가 쓰러지면 승리하고, 플레이어 팀 세 마리가 모두 쓰러지면 패배합니다. 서버와 클라이언트는 변경된 팀 메시지를 사용하므로 함께 다시 실행해야 합니다.
