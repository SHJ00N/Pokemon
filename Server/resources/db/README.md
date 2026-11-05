# 포켓몬 DB

`Pokemon.db`는 SQLite 데이터베이스입니다. 서버가 시작할 때 읽기 전용으로 열어 메모리에 적재하며, 실행 중에는 수정하지 않습니다. DB를 바꾼 뒤에는 서버를 다시 빌드·시작해야 합니다.

| 항목 | 개수 |
| --- | --- |
| 종 (전국도감 1~1025번) | 1,025 |
| 폼 (기본 폼 + 리전폼·메가진화·거다이맥스 등) | 1,323 (팀에 넣을 수 있는 폼 1,139) |
| 진화 관계 (폼 단위) | 527 |
| 전투 중 폼 체인지 | 163 |
| 특성 / 포켓몬별 특성 | 313 / 2,883 |
| 도구 (지닐 수 있는 도구, 메가스톤 등) | 396 |
| 기술 (Z기술 53개·다이맥스기 19개 포함) | 919 |
| 습득 기술 연결 | 72,715 |
| 타입 / 상성 | 18 / 324 |
| 상태이상 / 기술별 상태이상 효과 | 8 / 87 |

**데이터 기준은 현재 세대입니다.** 페어리를 포함한 18타입과 6세대 이후 상성(고스트·악 → 강철 1배)을 쓰며, 포켓몬의 타입·종족값과 기술의 위력·명중률·PP도 원본의 현재 값입니다. 전투 계산(데미지 공식, 상태이상 규칙 등)은 여전히 서버 코드의 5세대 규칙을 따릅니다.

## 다시 만들기

DB는 [build_db.py](../../tools/build_db.py)가 [PokéAPI CSV](https://github.com/PokeAPI/pokeapi/tree/e5b79b2e15d7c50735a945936afd4cd1cfd94eae/data/v2/csv)로 만듭니다. 외부 패키지 없이 Python 표준 라이브러리만 씁니다. 프로젝트 루트에서 실행합니다.

```bash
python3 Server/tools/build_db.py --download ~/pokeapi-csv --force
```

- `--download <폴더>`: 고정 커밋(`e5b79b2e15d7c50735a945936afd4cd1cfd94eae`, 2026-11-02)의 CSV 24개(약 12MB)를 그 폴더에 내려받은 뒤 DB를 만듭니다. 이미 받은 파일은 다시 받지 않습니다. 받은 CSV로 다시 만들 때는 `--source <폴더>`를 씁니다.
- `--force`가 없으면 기존 DB를 덮어쓰지 않습니다. 생성 중에는 임시 파일에 쓰고, 검사를 모두 통과한 뒤에 교체합니다.
- 같은 폴더의 [source_manifest.json](source_manifest.json)에 원본 커밋, 원본 CSV와 프로젝트 전용 CSV의 SHA-256, 테이블별 행 수, 아래 "직접 정한 것"의 목록이 저장됩니다.
- PokéAPI에 없는 프로젝트 전용 데이터는 [Server/tools/db_custom/](../../tools/db_custom)의 CSV에서 읽습니다.

| 파일 | 내용 |
| --- | --- |
| `type_colors.csv` | 타입별 터미널 표시 색 (페어리 `#D685AD` 추가) |
| `status_conditions.csv` | 상태이상 8종 (맹독은 프로젝트 전용 ID `1001`) |
| `move_status_effects.csv` | 기술별 상태이상 효과 87개 (기술 1~559번) |
| `form_names.csv` | 원본에 한국어 폼 이름이 없어 직접 적은 22개 |

생성 과정에서 종 번호 연속성, 모든 종에 도감번호와 같은 번호의 기본 폼이 있는지, 타입·종족값 범위, 324개 상성, 진화 관계의 순환 여부, 모든 메가진화에 메가스톤(또는 기술)이 연결됐는지, 모든 폼의 습득 기술, Z기술·다이맥스기를 배우는 폼이 없는지, 외래 키와 SQLite 무결성을 검사합니다. 하나라도 실패하면 DB를 만들지 않습니다.

## 테이블

```text
PokemonSpecies (종, 전국도감 번호)
  └─ Pokemon (폼) ──┬─ PokemonEvolution (진화 전 폼 → 진화 후 폼)
                    ├─ PokemonFormChange (전투 중 폼 체인지 → Item, PokemonMove)
                    ├─ PokemonAbility ── Ability
                    ├─ PokemonLearnset ── PokemonMove ── MoveStatusEffect ── StatusCondition
                    └─ Type ── TypeEffectiveness
Item (지닌 도구)
```

모든 이름은 한국어이고, 새 테이블에는 PokéAPI의 영문 식별자(`identifier`, 예: `charizard-mega-x`, `charizardite-x`)도 함께 저장해 코드와 원본에서 찾기 쉽게 했습니다.

## 종과 폼

**종(`PokemonSpecies`)과 폼(`Pokemon`)을 나눴습니다.** 전투에 쓰는 타입·종족값·특성·습득 기술은 폼마다 다르기 때문입니다. 예를 들어 라이츄(전기)와 알로라 라이츄(전기·에스퍼)는 같은 종(26번)의 다른 폼입니다.

`PokemonSpecies`

| 열 | 의미 |
| --- | --- |
| `species_id` | 전국도감 번호 1~1025 |
| `name` | 한국어 종 이름 |
| `generation` | 처음 나온 세대 1~9 |
| `gender_rate` | `-1` 무성, `0`~`8` 암컷 비율(8분의 n). 헤롱헤롱·유혹 등 성별 규칙을 넣을 때 씁니다 |
| `is_baby`, `is_legendary`, `is_mythical` | 아기·전설·환상 포켓몬 여부 |

`Pokemon`

| 열 | 의미 |
| --- | --- |
| `pokemon_id` | 폼 번호. **기본 폼은 전국도감 번호와 같고**, 다른 폼은 PokéAPI 번호(10001 이후)입니다. 그래서 이전 DB의 1~649번은 번호가 그대로입니다 |
| `species_id` | 종 |
| `name` | 표시 이름. 메가진화·원시회귀는 `메가리자몽X`, `원시가이오가`처럼 폼 이름, 나머지는 종 이름입니다 |
| `form_name` | 폼 이름 (`알로라의 모습`, `어택폼`, `거다이맥스` 등). 없으면 `NULL` |
| `form_kind` | 아래 표 |
| `region` | 리전폼의 지방 (`alola`, `galar`, `hisui`, `paldea`). 가라르 불비달마의 달마모드처럼 리전폼의 전투 폼에도 붙습니다 |
| `is_default` | 그 종의 기본 폼 |
| `is_battle_only` | 전투 중에만 되는 폼 |
| `is_selectable` | 팀에 넣을 수 있는 폼 |
| `type1_id`, `type2_id` | 타입. 단일 타입이면 `type2_id`가 `NULL` |
| `base_hp` … `base_speed` | 종족값 |
| `height`, `weight` | 키(0.1m), 몸무게(0.1kg). 안다리걸기·헤비봄버처럼 무게를 쓰는 기술에 씁니다 |
| `learnset_version_group` | 습득 기술을 가져온 게임 (아래 "습득 가능 기술") |

| `form_kind` | 개수 | 예 | 팀 선택 |
| --- | --- | --- | --- |
| `default` | 1,025 | 리자몽, 도롱마담(초목도롱) | O |
| `regional` | 57 | 알로라 라이츄, 가라르 나옹, 히스이 가디, 팔데아 켄타로스 | O |
| `alternate` | 80 | 테오키스 어택폼, 워시로토무, 오리진폼, 오거폰 가면 | 대부분 O |
| `mega` | 97 | 메가리자몽X, 메가픽시(레전즈 Z-A), 메가라이츄X | X |
| `primal` | 2 | 원시가이오가, 원시그란돈 | X |
| `gmax` | 34 | 리자몽 거다이맥스 | X |
| `battle` | 28 | 킬가르도 블레이드폼, 지우개굴닌자, 울트라네크로즈마 | X |

팀에 넣을 수 없는 `alternate`는 다음과 같습니다.

- **배틀 데이터가 기본 폼과 똑같은 폼 22개:** 타입·종족값·특성이 모두 같고 겉모습만 다른 폼입니다. 메테노의 유성 색, 코라이돈·미라이돈의 탈것 폼, 케르디오 각오의 모습 등. 목록이 같은 포켓몬으로 채워지지 않도록 뺐습니다.
- **무한다이노 무한다이맥스:** 게임에서 플레이어가 쓸 수 없는 폼입니다.

넣지 않은 폼은 토템 포켓몬 12개와, 옷을 입은 피카츄·모자 피카츄·레츠고 파트너 피카츄·이브이 16개입니다. 모두 이벤트·연출용입니다.

## 진화

**진화는 "진화 전 폼 → 진화 후 폼" 한 줄씩 저장합니다(`PokemonEvolution`).** 진화 단계를 0·1·2 같은 숫자로 저장하지 않습니다.

- **진화가 여러 갈래여도 그대로 담깁니다.** 이브이는 8줄, 피카츄는 라이츄·알로라 라이츄 2줄입니다.
- **나중에 앞·뒤 단계가 추가돼도 숫자를 고칠 필요가 없습니다.** 예를 들어 4세대에서 마그비(진화 전)와 마그마번(진화 후)이 생겨도 줄만 추가하면 됩니다. 단계는 저장하지 않고 계산합니다.
- **폼 단위입니다.** 가라르 파오리만 창파나이트로 진화하고, 알로라 나옹은 알로라 페르시온으로, 가라르 나옹은 나이킹으로 진화합니다. 종 단위로 저장하면 "파오리는 진화할 수 있다"가 되어 진화의휘석 같은 규칙이 틀립니다.
- 원본 `pokemon_evolution.csv`의 진화 조건(레벨·도구·지방 등)은 저장하지 않습니다. 전투에는 "진화할 수 있는지"와 "어디서 왔는지"만 필요하기 때문입니다. 같은 폼 쌍은 한 줄만 남깁니다.

`PokemonEvolutionInfo` 뷰가 단계를 계산합니다. 전투 전용 폼은 넣지 않습니다(원래 폼을 봅니다).

| 열 | 의미 |
| --- | --- |
| `stage` | 진화 전 단계 수. 피츄 0, 피카츄 1, 라이츄·알로라 라이츄 2 |
| `can_evolve` | 더 진화할 수 있으면 1. 최종 진화형은 0 |

```sql
-- 진화 전 단계 (진화의휘석, 이전 단계 기술 상속 등에 쓴다)
SELECT f.* FROM PokemonEvolution AS e JOIN Pokemon AS f ON f.pokemon_id = e.from_pokemon_id
WHERE e.to_pokemon_id = 26;

-- 진화할 수 있는 포켓몬인지
SELECT can_evolve FROM PokemonEvolutionInfo WHERE pokemon_id = 10166;   -- 가라르 파오리: 1

-- 최종 진화형만 (팀 선택 제한 등)
SELECT p.* FROM Pokemon AS p JOIN PokemonEvolutionInfo AS i USING (pokemon_id)
WHERE p.is_selectable = 1 AND i.can_evolve = 0;
```

## 전투 중 폼 체인지

메가진화·원시회귀·거다이맥스와, 특성·기술로 전투 중에 바뀌는 폼은 `PokemonFormChange`에 "바뀐 폼 ← 원래 폼"으로 저장합니다.

| 열 | 의미 |
| --- | --- |
| `pokemon_id` | 바뀐 폼 (메가리자몽X 등) |
| `base_pokemon_id` | 원래 폼. 원래 폼이 여럿이면 여러 줄입니다 (울트라네크로즈마 ← 황혼의 갈기·새벽의 날개) |
| `method` | `mega`(97), `primal`(2), `gmax`(34), `battle`(30) |
| `item_id` | 필요한 도구. 메가스톤 92개 전부 연결했고, 원시회귀는 쪽빛구슬·주홍구슬, 자시안·자마젠타는 녹슨검·녹슨방패입니다 |
| `move_id` | 필요한 기술. 레쿠쟈의 메가진화는 메가스톤 대신 화룡점정입니다 |

- 메가스톤 하나가 여러 폼에 쓰일 수 있습니다. 냐오닉스나이트는 수컷·암컷 메가냐오닉스, 싸리용나이트는 싸리용 세 모습의 메가진화에 연결됩니다.
- 거다이맥스는 도구가 필요 없습니다. 다이맥스·거다이맥스 기술(`move_kind = 'max'`)도 기술 목록에 있지만 아직 기술과 폼을 잇지 않았습니다.
- 전투 전용 폼은 원래 폼의 습득 기술을 그대로 씁니다. 특성은 폼마다 원본 값을 씁니다.

```sql
-- 메가리자몽X가 되려면
SELECT b.name AS 원래폼, i.name AS 도구, fc.method
FROM PokemonFormChange AS fc
JOIN Pokemon AS b ON b.pokemon_id = fc.base_pokemon_id
LEFT JOIN Item AS i ON i.item_id = fc.item_id
WHERE fc.pokemon_id = 10034;   -- 리자몽, 리자몽나이트X, mega

-- 이 포켓몬이 메가진화할 수 있는 폼들
SELECT m.name, i.name AS 메가스톤 FROM PokemonFormChange AS fc
JOIN Pokemon AS m ON m.pokemon_id = fc.pokemon_id LEFT JOIN Item AS i ON i.item_id = fc.item_id
WHERE fc.base_pokemon_id = 6 AND fc.method = 'mega';
```

## 특성과 도구

아직 전투 로직에서 쓰지 않지만, 특성·지닌 도구 규칙을 넣을 때 바로 쓸 수 있도록 넣었습니다.

- `Ability`: 포켓몬이 가진 특성 313개. `PokemonAbility`는 폼별 특성이며 `slot` 1·2가 일반 특성, 3이 숨겨진 특성(`is_hidden = 1`)입니다.
- `Item`: 지닐 수 있는 도구(구애·생명의구슬·나무열매 등) 224개, 메가스톤 92개, Z크리스탈, 플레이트, 메모리, 쥬얼 등 396개. `category`는 PokéAPI 분류(`choice`, `held-items`, `mega-stones` 등), `fling_power`는 내던지기 위력, `is_holdable`은 지닐 수 있는 도구 여부입니다.

```sql
-- 리자몽의 특성
SELECT pa.slot, a.name, pa.is_hidden FROM PokemonAbility AS pa
JOIN Ability AS a USING (ability_id) WHERE pa.pokemon_id = 6 ORDER BY pa.slot;
```

## 기술

`PokemonMove`에 기술 1~919번을 모두 저장합니다. 이름은 모두 한국어이고, 분류는 `물리`·`특수`·`변화`입니다. 다크 포켓몬 전용 기술(콜로세움·XD) 18개는 넣지 않았습니다.

- `move_kind`: `normal`, `z`(Z기술 53개), `max`(다이맥스기 19개). Z기술과 다이맥스기는 어떤 포켓몬도 배우지 않으며, Z기술·다이맥스를 구현할 때 씁니다.
- `is_multi_hit`: 원본 `move_meta.csv`에서 `max_hits > 1`인 연속기 28개. 타격 횟수 규칙은 전투 로직에서 구현해야 합니다.
- `priority`: 우선도. `generation`: 처음 나온 세대.
- 위력·명중률이 원본에 없으면 `NULL`입니다.

## 습득 가능 기술

`PokemonLearnset`은 **각 폼이 마지막으로 나온 게임의 습득 기술**입니다. 그 게임의 레벨업·기술머신·교배·가르침 등 모든 경로를 합치며, 습득 레벨과 경로는 저장하지 않습니다. 어느 게임의 기술인지는 `Pokemon.learnset_version_group`에 있습니다.

| 기준 게임 | 팀에 넣을 수 있는 폼 |
| --- | --- |
| 포켓몬 챔피언스 (`champions`) | 235 |
| 스칼렛·바이올렛 (`scarlet-violet`) | 634 |
| 소드·실드 (`sword-shield`) | 138 |
| 브릴리언트다이아몬드·샤이닝펄 (`brilliant-diamond-shining-pearl`) | 125 |
| 울트라썬·울트라문 (`ultra-sun-ultra-moon`) | 7 |

- 배틀 방식이 다른 외전(레츠고 피카츄·이브이, 레전즈 아르세우스, 레전즈 Z-A, 콜로세움·XD)은 다른 게임에 없는 폼에만 씁니다. 현재는 해당하는 폼이 없습니다.
- 진화 전 단계의 기술은 물려받지 않습니다. 진화 정보(`PokemonEvolution`)가 있으므로 나중에 상속 규칙을 넣을 수 있습니다.
- 이전 DB(블랙2·화이트2 레벨업 + 1~5세대 교배기·비전머신)와 기술 목록이 많이 다릅니다. 예를 들어 최근 게임에서 빠진 비전머신 괴력은 피카츄가 더 이상 배우지 못하고, 기술머신 기술은 새로 배웁니다.
- 습득 기술이 4개 미만이라 팀에 넣을 수 없는 포켓몬이 12종 있습니다: 메타몽·루브도·안농 각 1개, 캐터피·뿔충이·잉어킹 3개, 단데기·딱충이·실쿤·카스쿤 1개, 코스모그 2개, 코스모움 2개. 클라이언트가 선택할 때 안내합니다.

## 타입과 상성

`Type`은 18개 타입(노말~페어리)의 한국어 이름과 터미널 색, `TypeEffectiveness`는 18×18 = 324개 상성 배율(0, 0.5, 1, 2)입니다. 1배 상성도 행으로 있습니다. 테라스탈 전용 스텔라 타입은 아직 넣지 않았습니다.

```sql
SELECT e.multiplier FROM TypeEffectiveness AS e
JOIN Type AS a ON a.type_id = e.attack_type_id JOIN Type AS d ON d.type_id = e.defend_type_id
WHERE a.name = '드래곤' AND d.name = '페어리';   -- 0.0
```

## 상태이상과 기술별 적용 효과

`StatusCondition`은 상태이상 정의, `MoveStatusEffect`는 기술과 상태이상의 연결입니다. 프로젝트에서 직접 정리한 데이터라 `db_custom/`의 CSV로 보관하며, 이전 DB와 값이 같습니다. **기술 1~559번만 연결되어 있습니다.** 560번 이후 기술의 상태이상 효과는 아직 없습니다.

| 열 | 의미 |
| --- | --- |
| `status_id` | 상태이상. 마비·잠듦·얼음·화상·독·혼란·헤롱헤롱(1~7, PokéAPI 번호)과 맹독(프로젝트 전용 `1001`) |
| `chance` | 적용 확률(%) |
| `target` | `opponent`(상대) 또는 `self`(자신) |
| `timing` | `on_hit`(적중 후), `on_use`(사용 성공 시), `after_sequence`(연속 턴 기술 종료 후), `end_next_turn`(다음 턴 종료 시) |
| `effect_group` | `0`은 개별 효과. 같은 양수 그룹은 그중 하나만 추첨 (트라이어택: 마비·얼음·화상 각 20/3%) |

- 변화 기술은 상태 부여가 주 효과이면 100%로 저장했습니다. 명중 판정과 타입·특성 면역은 전투 로직이 처리합니다.
- 맹독(92)과 맹독엄니(305)는 맹독, 잠자기(156)는 자신에게 잠듦(`on_use`), 난동부리기·꽃잎댄스·역린은 자신에게 혼란(`after_sequence`), 하품(281)은 상대에게 잠듦(`end_next_turn`)입니다.

```csharp
foreach (var effect in data.MoveStatusEffectsByMoveId[85])   // 10만볼트
{
    var status = data.StatusConditionsById[effect.StatusId];  // 마비, effect.Chance: 10
}
```

## 이전 DB(5세대 649마리)와 달라진 점

- **번호가 같습니다.** 1~649번 포켓몬과 1~559번 기술의 번호·이름·종족값은 그대로입니다. 클라이언트 그림 파일(`0001.txt` …)도 그대로 쓸 수 있습니다.
- **현재 세대 타입으로 돌아간 것:**
  - 포켓몬 22종이 페어리 타입이 됐습니다: 삐삐·픽시·푸린·푸크린·마임맨·삐·푸푸린·토게피·토게틱·마릴·마릴리·블루·그랑블루·랄토스·킬리아·가디안·루리리·입치트·흉내내·토게키스·소미안·엘풍.
  - 기술 3개도 페어리가 됐습니다: 천사의키스·애교부리기·달빛.
  - 상성은 고스트·악 → 강철이 0.5배에서 1배로 바뀌었습니다.
- **습득 기술 기준이 바뀌었습니다** (위 "습득 가능 기술").
- 타입 색, 상태이상, 기술별 상태이상 효과는 이전 값과 같습니다.

## 원본에 없어 직접 정한 것

`build_db.py`가 매번 다시 확인해 `source_manifest.json`의 `notes`에 기록합니다.

- **한국어 폼 이름 (`db_custom/form_names.csv` 22개):** 팔데아 켄타로스 세 품종, 돌핀맨 마이티폼, 테라파고스 폼, 오리진폼 등. 공식 이름과 다르면 이 파일을 고친 뒤 다시 만드세요.
- **규칙으로 만든 폼 이름:** 레전즈 Z-A 이후 메가진화 49개(`메가픽시`, `메가라이츄X` …), 거다이맥스 34개(`거다이맥스`), 히스이·팔데아 리전폼 20개(`히스이의 모습` …)입니다. 이 폼들은 원본에 한국어 이름이 없습니다.
- **영어 이름을 쓴 특성 3개:** `eelevate`, `fire-mane`, `aura-guard`. 최근 메가진화의 새 특성이라 원본에 한국어 이름이 없습니다.
- **특성 정보가 없는 메가진화 9개:** 메가지가르데·메가히드런·메가다크라이·메가마기아나(2)·메가제라오라·메가싸리용(3). 원본에 아직 없습니다.
- **연결하지 못한 도구:** 울트라네크로즈마의 울트라네크로Z는 원본 도구 목록에 없어서 도구 없이 폼 체인지로만 연결했습니다.
- **원래 폼을 직접 지정한 전투 폼:** 지우개굴닌자(← 유대변화 개굴닌자), 메가플라엣테(← 영원의 꽃 플라엣테), 울트라네크로즈마, 퍼펙트폼 지가르데(← 스웜체인지 지가르데), 스텔라폼 테라파고스. 나머지는 이름 규칙(같은 지방, 이름 앞부분이 가장 많이 같은 폼)으로 찾습니다.
- **전투 폼으로 표시:** 메테노 코어(7색). 원본에 전투 전용 표시가 없습니다.

## 출처

- [PokéAPI](https://github.com/PokeAPI/pokeapi) CSV, 커밋 `e5b79b2e15d7c50735a945936afd4cd1cfd94eae` (2026-11-02). 내려받은 날짜: 2026-11-05.
- 종 이름·타입과 메가진화 이름·타입은 [포켓몬 위키 전국도감](https://pokemon.fandom.com/ko/wiki/%EC%A0%84%EA%B5%AD%EB%8F%84%EA%B0%90)(2026-11-05 확인)과 대조했습니다.
  - 1,025종의 이름이 모두 같습니다. 타입은 부르롱·부르르룸의 순서만 다릅니다: 위키는 독·강철, 원본은 강철·독(공식 순서)이며 원본을 따랐습니다.
  - 위키에 있는 메가진화·원시회귀 49개의 이름과 타입도 같습니다. 레전즈 Z-A 이후 메가진화는 위키 목록에 아직 없습니다.

## 조회 예시

```sql
-- 팀에 넣을 수 있는 폼 (도감번호 순)
SELECT pokemon_id, name, form_name FROM Pokemon WHERE is_selectable = 1 ORDER BY species_id, pokemon_id;

-- 한 종의 모든 폼
SELECT pokemon_id, name, form_name, form_kind, is_selectable FROM Pokemon WHERE species_id = 6;

-- 피카츄가 배울 수 있는 기술
SELECT m.move_id, m.name, t.name AS type FROM PokemonLearnset AS l
JOIN PokemonMove AS m USING (move_id) JOIN Type AS t ON t.type_id = m.type_id
WHERE l.pokemon_id = 25 ORDER BY m.move_id;
```
