#!/usr/bin/env python3
"""포켓몬 DB(Server/resources/db/Pokemon.db)를 PokéAPI CSV로 다시 만든다.

사용법 (프로젝트 루트에서):
    python3 Server/tools/build_db.py --download <원본_폴더>   # 고정 커밋의 CSV를 내려받고 DB 생성
    python3 Server/tools/build_db.py --source <원본_폴더>     # 이미 받은 CSV로 DB 생성

외부 패키지 없이 표준 라이브러리만 쓴다. 기존 DB는 --force를 줄 때만 덮어쓴다.
PokéAPI에 없는 프로젝트 전용 데이터(타입 색상, 상태이상, 기술 1~559번의 상태이상 효과)는
Server/tools/db_custom/의 CSV에서 읽는다. 기술의 능력치 변화·풀죽음·흡수·연속기 등과 560번 이후 기술의
상태이상은 PokéAPI move_meta로 채운다. 데이터는 6세대(XY·ORAS) 기준이다: 팀에 넣을 수 있는 폼, 습득 기술,
기술 값(move_changelog), 종족값(pokemon_stats_past). 데이터 범위와 규칙은 Server/resources/db/README.md에 정리했다.
"""
import argparse
import csv
import hashlib
import json
import shutil
import sqlite3
import subprocess
import sys
import urllib.error
import urllib.request
from collections import defaultdict
from pathlib import Path

POKEAPI_COMMIT = "e5b79b2e15d7c50735a945936afd4cd1cfd94eae"   # 2026-11-02
POKEAPI_CSV_URL = f"https://raw.githubusercontent.com/PokeAPI/pokeapi/{POKEAPI_COMMIT}/data/v2/csv/"
SOURCE_FILES = [
    "pokemon_species", "pokemon_species_names", "pokemon", "pokemon_forms", "pokemon_form_names",
    "pokemon_types", "pokemon_stats", "types", "type_names", "type_efficacy",
    "moves", "move_names", "move_meta", "pokemon_moves", "version_groups",
    "move_meta_stat_changes", "move_meta_ailments", "move_meta_categories", "move_targets", "stats",
    "abilities", "ability_names", "pokemon_abilities",
    "items", "item_names", "item_categories", "item_flag_map", "item_flags",
    "pokemon_evolution",
    "move_changelog", "pokemon_stats_past", "move_flag_map", "move_flags",
]
KO, EN = "3", "9"   # PokéAPI local_language_id

TOOLS_DIR = Path(__file__).resolve().parent
DEFAULT_OUT = TOOLS_DIR.parent / "resources" / "db" / "Pokemon.db"
CUSTOM_DIR = TOOLS_DIR / "db_custom"

TYPE_IDS = range(1, 19)        # 노말~페어리. 스텔라(19)는 테라스탈 전용이라 아직 넣지 않는다.
REGIONS = ("alola", "galar", "hisui", "paldea")
CATEGORY_KO = {"1": "변화", "2": "물리", "3": "특수"}   # move_damage_classes

# 이 게임은 6세대(XY·ORAS) 기준이다 (README "6세대 기준"). 6세대 게임에 있던 폼만 팀에 넣을 수 있고,
# 습득 기술은 XY·ORAS를 합친 것에 진화 전 단계의 기술(알 기술 포함)을 더한다. 기술 값과 종족값도 6세대 값으로 되돌린다.
GEN6_VERSION_GROUPS = ("x-y", "omega-ruby-alpha-sapphire")
GEN6 = 6

# 6세대에 없던 폼의 습득 기술: 각 폼이 마지막으로 나온 '일반 배틀' 게임의 습득 기술 (팀에는 넣을 수 없다).
# 배틀 방식이 다른 외전은 다른 게임에 없을 때만 쓴다.
SPIN_OFF_VERSION_GROUPS = {"colosseum", "xd", "lets-go-pikachu-lets-go-eevee", "legends-arceus",
                           "legends-za", "mega-dimension"}
EXCLUDED_LEARN_METHODS = {"5", "7", "8", "9"}   # 스타디움 파도타기, 콜로세움·XD 다크 포켓몬 관련

# Z기술: 이름만으로 구분되지 않아 번호로 정한다. 다이맥스기는 identifier가 max-로 시작한다.
Z_MOVE_IDS = set(range(622, 659)) | set(range(695, 704)) | {719} | set(range(723, 729))

# 게임 안 이벤트·연출용 폼은 넣지 않는다 (배틀 데이터가 기본 폼과 같거나 일반 배틀에서 쓸 수 없음).
EXCLUDED_POKEMON = {
    "pikachu-rock-star", "pikachu-belle", "pikachu-pop-star", "pikachu-phd", "pikachu-libre",
    "pikachu-cosplay", "pikachu-original-cap", "pikachu-hoenn-cap", "pikachu-sinnoh-cap",
    "pikachu-unova-cap", "pikachu-kalos-cap", "pikachu-alola-cap", "pikachu-partner-cap",
    "pikachu-world-cap", "pikachu-starter", "eevee-starter",
}
# PokéAPI에 전투 전용 표시가 없지만 전투 중에만 되는 폼
EXTRA_BATTLE_ONLY = {f"minior-{color}" for color in ("red", "orange", "yellow", "green", "blue", "indigo", "violet")}
# 팀에 넣을 수 없는 폼 (게임에서 플레이어가 쓸 수 없음)
UNAVAILABLE = {"eternatus-eternamax"}
# 전투 중 폼 체인지의 원래 폼. 이름 규칙(같은 지방·가장 긴 이름 앞부분)으로 찾을 수 없는 경우만 적는다.
FORM_CHANGE_BASES = {
    "greninja-ash": ["greninja-battle-bond"],
    "floette-mega": ["floette-eternal"],
    "necrozma-ultra": ["necrozma-dusk", "necrozma-dawn"],
    "zygarde-complete": ["zygarde-50-power-construct", "zygarde-10-power-construct"],
    "terapagos-stellar": ["terapagos-terastal"],
}
FORM_CHANGE_ITEMS = {   # 메가스톤 외에 폼 체인지에 필요한 도구
    "kyogre-primal": "blue-orb", "groudon-primal": "red-orb",
    "zacian-crowned": "rusted-sword", "zamazenta-crowned": "rusted-shield",
    "necrozma-ultra": "ultranecrozium-z",
}
FORM_CHANGE_MOVES = {"rayquaza-mega": "dragon-ascent"}   # 메가스톤 없이 기술로 메가진화
REGION_KO = {"alola": "알로라", "galar": "가라르", "hisui": "히스이", "paldea": "팔데아"}
# 배틀에 쓰이는 지닌 도구: 지닐 수 있는 도구 + 폼 체인지·Z기술 관련 분류
ITEM_CATEGORIES = {"mega-stones", "z-crystals", "memories", "plates", "jewels", "species-specific"}

# 기술 부가 효과 (move_meta)
STAT_IDENTIFIERS = {"attack", "defense", "special-attack", "special-defense", "speed", "accuracy", "evasion"}
SELF_TARGETS = {"user", "users-field", "user-and-allies", "user-or-ally", "ally", "all-allies"}   # move_targets
AUTO_STATUS_FROM = 560   # 이 번호부터 상태이상 효과를 move_meta로 채운다 (1~559번은 db_custom CSV)
CONDITIONAL_AILMENT_MOVES = {807}   # 질투의불꽃: 그 턴에 능력치가 오른 상대만 화상이라 자동으로 넣지 않는다

SCHEMA = """
CREATE TABLE Type (
    type_id INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    color_rgb TEXT NOT NULL CHECK (color_rgb GLOB '#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]')
);
CREATE TABLE TypeEffectiveness (
    attack_type_id INTEGER NOT NULL REFERENCES Type(type_id),
    defend_type_id INTEGER NOT NULL REFERENCES Type(type_id),
    multiplier REAL NOT NULL CHECK (multiplier IN (0.0, 0.5, 1.0, 2.0)),
    PRIMARY KEY (attack_type_id, defend_type_id)
);
CREATE TABLE PokemonSpecies (
    species_id INTEGER PRIMARY KEY,                 -- 전국도감 번호
    identifier TEXT NOT NULL UNIQUE,                -- PokéAPI 영문 식별자
    name TEXT NOT NULL,
    generation INTEGER NOT NULL CHECK (generation BETWEEN 1 AND 9),
    gender_rate INTEGER NOT NULL CHECK (gender_rate BETWEEN -1 AND 8),   -- -1: 무성, 0~8: 암컷 비율(8분의 n)
    is_baby INTEGER NOT NULL CHECK (is_baby IN (0, 1)),
    is_legendary INTEGER NOT NULL CHECK (is_legendary IN (0, 1)),
    is_mythical INTEGER NOT NULL CHECK (is_mythical IN (0, 1))
);
CREATE TABLE Pokemon (
    pokemon_id INTEGER PRIMARY KEY,                 -- 기본 폼 = 전국도감 번호, 다른 폼 = 10001 이후 (PokéAPI 번호)
    identifier TEXT NOT NULL UNIQUE,
    species_id INTEGER NOT NULL REFERENCES PokemonSpecies(species_id),
    name TEXT NOT NULL,                             -- 표시 이름 (메가리자몽X, 리전폼은 종 이름 그대로)
    form_name TEXT,                                 -- 폼 이름 (알로라의 모습, 어택폼 등). 없으면 NULL
    form_kind TEXT NOT NULL CHECK (form_kind IN ('default', 'regional', 'alternate', 'mega', 'primal', 'gmax', 'battle')),
    region TEXT CHECK (region IN ('alola', 'galar', 'hisui', 'paldea')),
    is_default INTEGER NOT NULL CHECK (is_default IN (0, 1)),
    is_battle_only INTEGER NOT NULL CHECK (is_battle_only IN (0, 1)),
    is_selectable INTEGER NOT NULL CHECK (is_selectable IN (0, 1)),
    type1_id INTEGER NOT NULL REFERENCES Type(type_id),
    type2_id INTEGER REFERENCES Type(type_id),
    base_hp INTEGER NOT NULL,
    base_attack INTEGER NOT NULL,
    base_defense INTEGER NOT NULL,
    base_sp_attack INTEGER NOT NULL,
    base_sp_defense INTEGER NOT NULL,
    base_speed INTEGER NOT NULL,
    height INTEGER NOT NULL,                        -- 0.1m 단위
    weight INTEGER NOT NULL,                        -- 0.1kg 단위 (안다리걸기·헤비봄버 등)
    learnset_version_group TEXT NOT NULL,           -- 습득 기술을 가져온 게임 (전투 전용 폼은 원래 폼의 게임)
    CHECK (type2_id IS NULL OR type2_id <> type1_id),
    CHECK (is_selectable = 0 OR is_battle_only = 0)
);
CREATE INDEX IX_Pokemon_Species ON Pokemon(species_id);
CREATE TABLE PokemonEvolution (
    from_pokemon_id INTEGER NOT NULL REFERENCES Pokemon(pokemon_id),   -- 진화 전 폼
    to_pokemon_id INTEGER NOT NULL REFERENCES Pokemon(pokemon_id),     -- 진화 후 폼
    PRIMARY KEY (from_pokemon_id, to_pokemon_id),
    CHECK (from_pokemon_id <> to_pokemon_id)
);
CREATE INDEX IX_PokemonEvolution_To ON PokemonEvolution(to_pokemon_id);
CREATE TABLE Ability (
    ability_id INTEGER PRIMARY KEY,
    identifier TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    generation INTEGER NOT NULL
);
CREATE TABLE PokemonAbility (
    pokemon_id INTEGER NOT NULL REFERENCES Pokemon(pokemon_id),
    slot INTEGER NOT NULL CHECK (slot IN (1, 2, 3)),                 -- 3: 숨겨진 특성
    ability_id INTEGER NOT NULL REFERENCES Ability(ability_id),
    is_hidden INTEGER NOT NULL CHECK (is_hidden IN (0, 1)),
    PRIMARY KEY (pokemon_id, slot)
);
CREATE INDEX IX_PokemonAbility_Ability ON PokemonAbility(ability_id);
CREATE TABLE Item (
    item_id INTEGER PRIMARY KEY,
    identifier TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    category TEXT NOT NULL,                         -- PokéAPI 분류 (mega-stones, choice, held-items 등)
    fling_power INTEGER,                            -- 내던지기 위력. 없으면 NULL
    is_holdable INTEGER NOT NULL CHECK (is_holdable IN (0, 1))
);
CREATE TABLE PokemonMove (
    move_id INTEGER NOT NULL PRIMARY KEY,
    identifier TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    type_id INTEGER NOT NULL REFERENCES Type(type_id),
    category TEXT NOT NULL CHECK (category IN ('물리', '특수', '변화')),
    power INTEGER,
    accuracy INTEGER,
    pp INTEGER NOT NULL,
    priority INTEGER NOT NULL DEFAULT 0,
    is_multi_hit INTEGER NOT NULL DEFAULT 0 CHECK (is_multi_hit IN (0, 1)),
    generation INTEGER NOT NULL,
    move_kind TEXT NOT NULL CHECK (move_kind IN ('normal', 'z', 'max'))   -- z: Z기술, max: 다이맥스기 (배울 수 없음)
);
CREATE TABLE PokemonFormChange (
    pokemon_id INTEGER NOT NULL REFERENCES Pokemon(pokemon_id),        -- 전투 중에 바뀐 폼
    base_pokemon_id INTEGER NOT NULL REFERENCES Pokemon(pokemon_id),   -- 바뀌기 전 폼
    method TEXT NOT NULL CHECK (method IN ('mega', 'primal', 'gmax', 'battle')),
    item_id INTEGER REFERENCES Item(item_id),                          -- 필요한 도구 (메가스톤 등)
    move_id INTEGER REFERENCES PokemonMove(move_id),                   -- 필요한 기술 (레쿠쟈: 화룡점정)
    PRIMARY KEY (pokemon_id, base_pokemon_id)
);
CREATE TABLE PokemonLearnset (
    pokemon_id INTEGER NOT NULL REFERENCES Pokemon(pokemon_id),
    move_id INTEGER NOT NULL REFERENCES PokemonMove(move_id),
    PRIMARY KEY (pokemon_id, move_id)
);
CREATE INDEX IX_PokemonLearnset_Move ON PokemonLearnset(move_id);
CREATE TABLE StatusCondition (
    status_id INTEGER NOT NULL PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL UNIQUE
);
CREATE TABLE MoveStatusEffect (
    move_id INTEGER NOT NULL REFERENCES PokemonMove(move_id),
    status_id INTEGER NOT NULL REFERENCES StatusCondition(status_id),
    chance REAL NOT NULL CHECK (chance > 0 AND chance <= 100),
    target TEXT NOT NULL DEFAULT 'opponent' CHECK (target IN ('opponent', 'self')),
    timing TEXT NOT NULL DEFAULT 'on_hit' CHECK (timing IN ('on_hit', 'on_use', 'after_sequence', 'end_next_turn')),
    effect_group INTEGER NOT NULL DEFAULT 0 CHECK (effect_group >= 0),
    PRIMARY KEY (move_id, status_id, target, timing)
);
CREATE INDEX IX_MoveStatusEffect_Status ON MoveStatusEffect(status_id);
CREATE TABLE MoveMeta (
    move_id INTEGER NOT NULL PRIMARY KEY REFERENCES PokemonMove(move_id),
    category TEXT NOT NULL,                         -- PokéAPI move_meta_categories (damage, net-good-stats, damage-lower 등)
    target TEXT NOT NULL,                           -- PokéAPI move_targets (selected-pokemon, user, all-opponents 등)
    ailment TEXT,                                   -- PokéAPI move_meta_ailments (paralysis, trap, leech-seed 등). 없으면 NULL
    ailment_chance INTEGER NOT NULL,                -- 상태이상 확률(%). 0이면 변화 기술의 주 효과
    min_hits INTEGER,                               -- 연속기 타격 횟수. 연속기가 아니면 NULL
    max_hits INTEGER,
    min_turns INTEGER,                              -- 여러 턴 이어지는 효과의 턴 수(난동·조이기 등). 없으면 NULL
    max_turns INTEGER,
    drain INTEGER NOT NULL,                         -- 준 데미지의 %만큼 HP 흡수(양수)·반동(음수)
    healing INTEGER NOT NULL,                       -- 최대 HP의 %만큼 회복(양수)·자신 피해(음수)
    crit_rate INTEGER NOT NULL,                     -- 급소 랭크 보정 (급소율이 높은 기술 1)
    flinch_chance INTEGER NOT NULL,                 -- 풀죽음 확률(%)
    stat_chance INTEGER NOT NULL                    -- 능력치 변화 확률(%). 0이면 항상 (변화 기술)
);
CREATE TABLE MoveStatChange (
    move_id INTEGER NOT NULL REFERENCES PokemonMove(move_id),
    stat TEXT NOT NULL CHECK (stat IN ('attack', 'defense', 'special-attack', 'special-defense', 'speed',
                                       'accuracy', 'evasion')),
    change INTEGER NOT NULL CHECK (change <> 0),   -- 랭크 변화 단계 (+2, -1 등)
    chance INTEGER NOT NULL CHECK (chance > 0 AND chance <= 100),
    target TEXT NOT NULL CHECK (target IN ('self', 'opponent')),
    PRIMARY KEY (move_id, stat)
);
-- 기술 플래그 (PokéAPI move_flags 식별자): contact(접촉), snatch(가로채기 대상), mirror(따라하기 대상), heal(회복 기술),
-- sound(소리), powder(가루), punch, bite, pulse, protect(방어에 막힘), reflectable(매직코트) 등
CREATE TABLE MoveFlag (
    move_id INTEGER NOT NULL REFERENCES PokemonMove(move_id),
    flag TEXT NOT NULL,
    PRIMARY KEY (move_id, flag)
);

-- 진화 단계(0: 진화 전 단계가 없음)와 진화 가능 여부. 전투 전용 폼은 원래 폼을 본다.
CREATE VIEW PokemonEvolutionInfo AS
WITH RECURSIVE Stage(pokemon_id, stage) AS (
    SELECT pokemon_id, 0 FROM Pokemon
    WHERE is_battle_only = 0 AND pokemon_id NOT IN (SELECT to_pokemon_id FROM PokemonEvolution)
    UNION ALL
    SELECT e.to_pokemon_id, s.stage + 1
    FROM PokemonEvolution AS e JOIN Stage AS s ON s.pokemon_id = e.from_pokemon_id
)
SELECT s.pokemon_id,
       MAX(s.stage) AS stage,
       EXISTS (SELECT 1 FROM PokemonEvolution AS e WHERE e.from_pokemon_id = s.pokemon_id) AS can_evolve
FROM Stage AS s
GROUP BY s.pokemon_id;
"""


class BuildError(Exception):
    pass


def check(condition, message):
    if not condition:
        raise BuildError(message)


def download(source_dir: Path):
    source_dir.mkdir(parents=True, exist_ok=True)
    for name in SOURCE_FILES:
        target = source_dir / f"{name}.csv"
        if target.exists():
            continue
        print(f"내려받는 중: {name}.csv")
        url = POKEAPI_CSV_URL + f"{name}.csv"
        partial = target.with_suffix(".part")   # 받다가 실패해도 반쯤 받은 파일이 원본으로 남지 않게 한다.
        try:
            with urllib.request.urlopen(url) as response:
                partial.write_bytes(response.read())
        except urllib.error.URLError as error:
            # python.org의 macOS용 Python은 인증서가 설치되지 않아 HTTPS 확인에 실패할 수 있다. 시스템 curl로 다시 받는다.
            if shutil.which("curl") is None:
                raise BuildError(f"{name}.csv를 내려받지 못했습니다: {error}")
            result = subprocess.run(["curl", "-fsSL", "-o", str(partial), url])
            if result.returncode != 0:
                raise BuildError(f"{name}.csv를 내려받지 못했습니다: {error} (curl 종료 코드 {result.returncode})")
        partial.replace(target)


def read_csv(path: Path):
    with open(path, encoding="utf-8", newline="") as file:
        return list(csv.DictReader(file))


def localized(rows, key, field="name"):
    """{id: 한국어 이름} (없으면 영어)과 영어로 대신한 id 목록."""
    names, english = {}, {}
    for row in rows:
        if row["local_language_id"] == KO and row[field].strip():
            names[row[key]] = row[field].strip()
        elif row["local_language_id"] == EN and row[field].strip():
            english[row[key]] = row[field].strip()
    fallback = [k for k in english if k not in names]
    for k in fallback:
        names[k] = english[k]
    return names, fallback


def token_prefix(a: str, b: str) -> int:
    count = 0
    for x, y in zip(a.split("-"), b.split("-")):
        if x != y:
            break
        count += 1
    return count


def common_prefix(a: str, b: str) -> int:
    count = 0
    for x, y in zip(a, b):
        if x != y:
            break
        count += 1
    return count


def build(source: Path, out: Path, force: bool):
    def src(name):
        path = source / f"{name}.csv"
        check(path.exists(), f"원본 파일이 없습니다: {path}")
        return read_csv(path)

    report = {}

    # ── 타입·상성 ───────────────────────────────────────────────
    type_names, _ = localized(src("type_names"), "type_id")
    colors = {int(r["type_id"]): r["color_rgb"] for r in read_csv(CUSTOM_DIR / "type_colors.csv")}
    types = [(t, type_names[str(t)], colors[t]) for t in TYPE_IDS]
    effectiveness = [(int(r["damage_type_id"]), int(r["target_type_id"]), int(r["damage_factor"]) / 100)
                     for r in src("type_efficacy")
                     if int(r["damage_type_id"]) in TYPE_IDS and int(r["target_type_id"]) in TYPE_IDS]
    check(len(effectiveness) == 18 * 18, f"상성 개수가 324가 아닙니다: {len(effectiveness)}")

    # ── 종 ─────────────────────────────────────────────────────
    species_names, _ = localized(src("pokemon_species_names"), "pokemon_species_id")
    species_rows = src("pokemon_species")
    species = {}
    for r in species_rows:
        species[r["id"]] = r
        check(r["id"] in species_names, f"한국어 종 이름이 없습니다: {r['identifier']}")
    ids = sorted(int(i) for i in species)
    check(ids == list(range(1, len(ids) + 1)), "종 번호가 1부터 연속되지 않습니다.")

    # ── 게임 버전 (6세대 기준) ─────────────────────────────────
    version_groups = {r["id"]: r for r in src("version_groups")}
    vg_order = {k: int(v["order"]) for k, v in version_groups.items()}
    gen6_vgs = {k for k, v in version_groups.items() if v["identifier"] in GEN6_VERSION_GROUPS}
    check(len(gen6_vgs) == len(GEN6_VERSION_GROUPS), "XY·ORAS 버전을 찾을 수 없습니다.")
    gen6_last = max(vg_order[k] for k in gen6_vgs)

    # ── 폼(포켓몬) ─────────────────────────────────────────────
    forms_by_pokemon = defaultdict(list)
    forms = {}
    for r in src("pokemon_forms"):
        forms[r["id"]] = r
        forms_by_pokemon[r["pokemon_id"]].append(r)
    form_names = {}
    for r in src("pokemon_form_names"):
        if r["local_language_id"] == KO:
            form_names[r["pokemon_form_id"]] = (r["form_name"].strip(), r["pokemon_name"].strip())
    type_rows = defaultdict(dict)
    for r in src("pokemon_types"):
        type_rows[r["pokemon_id"]][int(r["slot"])] = int(r["type_id"])
    stat_rows = defaultdict(dict)
    for r in src("pokemon_stats"):
        stat_rows[r["pokemon_id"]][int(r["stat_id"])] = int(r["base_stat"])
    # 6세대 종족값: pokemon_stats_past의 generation_id는 그 값이 쓰인 마지막 세대다.
    # 능력치마다 6세대 이상인 것 중 가장 이른 값이 6세대 값이다.
    gen6_stats = defaultdict(dict)   # pokemon_id → stat_id → (generation, 값)
    for r in src("pokemon_stats_past"):
        stat_id, generation = int(r["stat_id"]), int(r["generation_id"])
        if stat_id in range(1, 7) and generation >= GEN6:
            old = gen6_stats[r["pokemon_id"]].get(stat_id)
            if old is None or generation < old[0]:
                gen6_stats[r["pokemon_id"]][stat_id] = (generation, int(r["base_stat"]))
    stat_changes_gen6 = []
    for pid, changed in gen6_stats.items():
        if pid in stat_rows:
            for stat_id, (_, value) in changed.items():
                stat_rows[pid][stat_id] = value
            stat_changes_gen6.append(pid)
    ability_rows = defaultdict(list)
    for r in src("pokemon_abilities"):
        ability_rows[r["pokemon_id"]].append(r)

    # PokéAPI에 한국어 폼 이름이 없는 폼: 직접 적은 이름(db_custom/form_names.csv) → 규칙으로 만든 이름 순으로 쓴다.
    custom_form_names = {r["identifier"]: r["form_name"] for r in read_csv(CUSTOM_DIR / "form_names.csv")}
    generated_names = []

    pokemon = {}   # identifier → dict
    for r in src("pokemon"):
        ident = r["identifier"]
        if ident in EXCLUDED_POKEMON or "-totem" in ident:
            continue
        # 코라이돈·미라이돈의 탈것 폼처럼 기본 표시가 없는 폼은 첫 번째 폼을 쓴다.
        default_form = next((f for f in forms_by_pokemon[r["id"]] if f["is_default"] == "1"),
                            min(forms_by_pokemon[r["id"]], key=lambda f: int(f["form_order"])))
        form_name, pokemon_name = form_names.get(default_form["id"], ("", ""))
        battle_only = default_form["is_battle_only"] == "1" or ident in EXTRA_BATTLE_ONLY
        region = next((reg for reg in REGIONS if reg in ident.split("-")), None)
        if default_form["is_mega"] == "1" or "-mega" in ident:
            kind = "mega"
        elif ident.endswith("-primal"):
            kind = "primal"
        elif ident.endswith("-gmax"):
            kind = "gmax"
        elif battle_only:
            kind = "battle"
        elif r["is_default"] == "1":
            kind = "default"
        elif region:
            kind = "regional"
        else:
            kind = "alternate"
        t = type_rows[r["id"]]
        check(1 in t and all(x in TYPE_IDS for x in t.values()), f"{ident}의 타입이 올바르지 않습니다: {t}")
        s = stat_rows[r["id"]]
        check(sorted(s) == [1, 2, 3, 4, 5, 6] and all(1 <= v <= 255 for v in s.values()),
              f"{ident}의 종족값이 올바르지 않습니다: {s}")
        species_name = species_names[r["species_id"]]
        if not form_name and r["is_default"] == "0":
            suffix = ident.split("-")[-1].upper() if ident.split("-")[-1] in ("x", "y", "z") else ""
            form_name = (custom_form_names.get(ident)
                         or (f"메가{species_name}{suffix}" if kind == "mega" else None)
                         or (f"원시{species_name}" if kind == "primal" else None)
                         or ("거다이맥스" if kind == "gmax" else None)
                         or (f"{REGION_KO[region]}의 모습" if region else None))
            if form_name:
                generated_names.append(f"{ident}={form_name}")
        # 메가진화·원시회귀는 위키처럼 메가리자몽X·원시가이오가를 표시 이름으로 쓴다. 리전폼 등은 종 이름 그대로다.
        if kind == "mega":
            name = form_name if form_name and form_name.startswith("메가") else f"메가{species_name}"
        elif kind == "primal":
            name = f"원시{species_name}"
        else:
            name = pokemon_name or species_name
        pokemon[ident] = {
            "id": int(r["id"]), "identifier": ident, "species_id": int(r["species_id"]),
            "name": name, "form_name": form_name or None, "kind": kind, "region": region,
            "is_default": int(r["is_default"]), "battle_only": int(battle_only),
            "type1": t[1], "type2": t.get(2),
            "stats": [s[i] for i in range(1, 7)],
            "height": int(r["height"]), "weight": int(r["weight"]),
            "abilities": sorted((int(a["slot"]), int(a["ability_id"]), int(a["is_hidden"])) for a in ability_rows[r["id"]]),
            # 6세대 게임(XY·ORAS)까지 나온 폼인지
            "gen6": vg_order[default_form["introduced_in_version_group_id"]] <= gen6_last,
        }
    report["한국어 폼 이름이 없어 직접 적거나 규칙으로 만든 폼"] = generated_names
    by_id = {p["id"]: p for p in pokemon.values()}
    report["6세대 종족값으로 되돌린 폼"] = sorted(by_id[int(pid)]["identifier"] for pid in stat_changes_gen6
                                         if int(pid) in by_id)
    defaults = {p["species_id"]: p for p in pokemon.values() if p["is_default"]}
    check(sorted(defaults) == ids and all(p["id"] == s for s, p in defaults.items()),
          "모든 종에 도감번호와 같은 번호의 기본 폼이 하나씩 있어야 합니다.")
    # 최근에 추가된 메가진화 등은 원본에 특성이 아직 없다. 전투 전용 폼만 허용하고 보고한다.
    for p in pokemon.values():
        check(p["abilities"] or p["battle_only"], f"{p['identifier']}의 특성이 없습니다.")
    report["특성 정보가 없는 전투 전용 폼"] = [p["identifier"] for p in pokemon.values() if not p["abilities"]]

    # 팀에 넣을 수 있는 폼: 6세대 게임까지 나온 폼 중 전투 전용·사용 불가 폼과,
    # 배틀 데이터가 기본 폼과 똑같은 겉모습만 다른 폼은 뺀다.
    def battle_data(p):
        return (p["type1"], p["type2"], tuple(p["stats"]), tuple(a[1] for a in p["abilities"]))
    cosmetic = []
    for p in pokemon.values():
        same_as_default = not p["is_default"] and battle_data(p) == battle_data(defaults[p["species_id"]])
        if same_as_default and not p["battle_only"]:
            cosmetic.append(p["identifier"])
        p["selectable"] = int(p["gen6"] and not p["battle_only"] and p["identifier"] not in UNAVAILABLE
                              and not same_as_default)
    report["겉모습만 다른 폼(팀 선택 제외)"] = cosmetic
    report["7세대 이후에 나온 폼(팀 선택 제외)"] = sum(not p["gen6"] for p in pokemon.values())

    # ── 전투 중 폼 체인지 ─────────────────────────────────────
    items_rows = src("items")
    item_by_ident = {r["identifier"]: r for r in items_rows}
    categories = {r["id"]: r["identifier"] for r in src("item_categories")}
    mega_stones = [r for r in items_rows if categories[r["category_id"]] == "mega-stones"]
    moves_rows = src("moves")
    move_by_ident = {r["identifier"]: r for r in moves_rows}

    def mega_suffix(ident):
        last = ident.split("-")[-1]
        return last if last in ("x", "y", "z") else ""

    def find_stone(p):
        base_species = species[str(p["species_id"])]["identifier"]
        suffix = mega_suffix(p["identifier"])
        best, best_score = None, 0
        for stone in mega_stones:
            stem, stone_suffix = stone["identifier"], ""
            if stem[-2:] in ("-x", "-y", "-z"):
                stem, stone_suffix = stem[:-2], stem[-1]
            if stone_suffix != suffix:
                continue
            score = common_prefix(stem, base_species)
            if score > best_score:
                best, best_score = stone, score
        check(best is not None and best_score >= 4, f"{p['identifier']}의 메가스톤을 찾을 수 없습니다.")
        return int(best["id"])

    form_changes = []
    for p in pokemon.values():
        if not p["battle_only"]:
            continue
        if p["identifier"] in FORM_CHANGE_BASES:
            bases = [pokemon[b] for b in FORM_CHANGE_BASES[p["identifier"]]]
        else:
            # 같은 종의 전투 전용이 아닌 폼 중 같은 지방 → 이름 앞부분이 가장 많이 같은 폼 → 기본 폼 순으로 고른다.
            candidates = [c for c in pokemon.values()
                          if c["species_id"] == p["species_id"] and not c["battle_only"]]
            bases = [max(candidates, key=lambda c: (c["region"] == p["region"],
                                                   token_prefix(c["identifier"], p["identifier"]),
                                                   c["is_default"]))]
        method = p["kind"] if p["kind"] in ("mega", "primal", "gmax") else "battle"
        item_id = move_id = None
        if p["identifier"] in FORM_CHANGE_MOVES:
            move_id = int(move_by_ident[FORM_CHANGE_MOVES[p["identifier"]]]["id"])
        elif p["identifier"] in FORM_CHANGE_ITEMS:
            item = item_by_ident.get(FORM_CHANGE_ITEMS[p["identifier"]])   # 원본에 없는 도구도 있다.
            item_id = int(item["id"]) if item else None
            if item is None:
                report.setdefault("원본 도구 목록에 없어 연결하지 못한 폼 체인지 도구", []).append(
                    f"{p['identifier']}: {FORM_CHANGE_ITEMS[p['identifier']]}")
        elif method == "mega":
            item_id = find_stone(p)
        for base in bases:
            form_changes.append((p["id"], base["id"], method, item_id, move_id))
    used_stones = {fc[3] for fc in form_changes if fc[2] == "mega"}
    report["메가스톤과 연결되지 않은 메가진화"] = [by_id[fc[0]]["identifier"] for fc in form_changes
                                    if fc[2] == "mega" and fc[3] is None and fc[4] is None]
    report["메가진화와 연결되지 않은 메가스톤"] = [s["identifier"] for s in mega_stones if int(s["id"]) not in used_stones]
    check(not report["메가스톤과 연결되지 않은 메가진화"], "메가스톤이 없는 메가진화가 있습니다.")

    # ── 진화 (폼 단위) ─────────────────────────────────────────
    form_to_pokemon = {fid: int(f["pokemon_id"]) for fid, f in forms.items()}
    evolutions = set()
    for r in src("pokemon_evolution"):
        to_species = species[r["evolved_species_id"]]
        from_species_id = int(to_species["evolves_from_species_id"])
        from_id = form_to_pokemon[r["required_pokemon_form_id"]] if r["required_pokemon_form_id"] else from_species_id
        to_id = form_to_pokemon[r["evolved_pokemon_form_id"]] if r["evolved_pokemon_form_id"] else int(r["evolved_species_id"])
        if from_id in by_id and to_id in by_id:
            check(by_id[from_id]["species_id"] == from_species_id, f"진화 전 폼의 종이 다릅니다: {r['id']}")
            evolutions.add((from_id, to_id))
    graph = defaultdict(set)
    for a, b in evolutions:
        graph[a].add(b)

    def has_cycle():
        state = {}
        def visit(n):
            state[n] = 1
            for m in graph[n]:
                if state.get(m) == 1 or (m not in state and visit(m)):
                    return True
            state[n] = 2
            return False
        return any(n not in state and visit(n) for n in list(graph))
    check(not has_cycle(), "진화 관계에 순환이 있습니다.")

    # ── 기술 ──────────────────────────────────────────────────
    move_names, move_name_fallback = localized(src("move_names"), "move_id")
    multi_hit = {r["move_id"] for r in src("move_meta") if r["max_hits"] and int(r["max_hits"]) > 1}
    moves = {}
    for r in moves_rows:
        if int(r["type_id"]) not in TYPE_IDS:   # 다크 포켓몬 전용(콜로세움·XD) 기술
            continue
        kind = "z" if int(r["id"]) in Z_MOVE_IDS else "max" if r["identifier"].startswith("max-") else "normal"
        moves[int(r["id"])] = (
            int(r["id"]), r["identifier"], move_names[r["id"]], int(r["type_id"]),
            CATEGORY_KO[r["damage_class_id"]],
            int(r["power"]) if r["power"] else None,
            int(r["accuracy"]) if r["accuracy"] else None,
            int(r["pp"]) if r["pp"] else 0,
            int(r["priority"]), int(r["id"] in multi_hit), int(r["generation_id"]), kind)
    report["한국어 이름이 없어 영어로 쓴 기술"] = [m for m in move_name_fallback if int(m) in moves]

    # 6세대 기술 값: move_changelog의 각 행은 그 버전에서 바뀌기 '전'의 값이다.
    # 값마다 ORAS 다음에 처음 바뀐 행의 값이 6세대 값이다. 6세대까지 나온 기술만 되돌린다.
    changelog_fields = {"type_id": 3, "power": 5, "accuracy": 6, "pp": 7, "priority": 8}   # moves 튜플 위치
    gen6_values = {}
    for r in sorted(src("move_changelog"), key=lambda r: vg_order[r["changed_in_version_group_id"]]):
        mid = int(r["move_id"])
        if mid in moves and moves[mid][10] <= GEN6 and vg_order[r["changed_in_version_group_id"]] > gen6_last:
            for field in changelog_fields:
                if r[field]:
                    gen6_values.setdefault((mid, field), int(r[field]))
    reverted = defaultdict(list)
    for (mid, field), value in sorted(gen6_values.items()):
        row = list(moves[mid])
        if row[changelog_fields[field]] != value:
            reverted[mid].append(f"{field} {row[changelog_fields[field]]}→{value}")
            row[changelog_fields[field]] = value
            moves[mid] = tuple(row)
    report["6세대 값으로 되돌린 기술"] = [f"{moves[m][1]}: {', '.join(v)}" for m, v in sorted(reverted.items())]

    # ── 습득 기술 ──────────────────────────────────────────────
    learn = defaultdict(lambda: defaultdict(set))   # pokemon_id → version_group_id → moves
    with open(source / "pokemon_moves.csv", encoding="utf-8", newline="") as file:
        for r in csv.DictReader(file):
            if r["pokemon_move_method_id"] in EXCLUDED_LEARN_METHODS:
                continue
            move_id = int(r["move_id"])
            if move_id in moves:
                learn[int(r["pokemon_id"])][r["version_group_id"]].add(move_id)
    def gen6_learn(pid):
        return set().union(*(learn[pid][g] for g in gen6_vgs if g in learn[pid]))

    gen6_game = "+".join(GEN6_VERSION_GROUPS)
    learnsets, learnset_game = {}, {}
    borrowed = []
    for p in pokemon.values():
        if p["battle_only"]:
            continue
        # 6세대 폼: XY·ORAS 습득 기술을 합친다. 폼 자체의 기록이 없으면 같은 종 기본 폼의 기술을 쓴다.
        if p["gen6"]:
            own = gen6_learn(p["id"])
            learned = own or gen6_learn(defaults[p["species_id"]]["id"])
            if learned:
                if not own:
                    borrowed.append(p["identifier"])
                learnsets[p["id"]] = learned
                learnset_game[p["id"]] = gen6_game
                continue
        # 6세대에 없던 폼(팀에 넣을 수 없음): 마지막으로 나온 일반 배틀 게임 기준
        games = learn[p["id"]]
        check(games, f"{p['identifier']}의 습득 기술이 없습니다.")
        standard = [g for g in games if version_groups[g]["identifier"] not in SPIN_OFF_VERSION_GROUPS]
        latest = max(standard or games, key=lambda g: vg_order[g])
        learnsets[p["id"]] = games[latest]
        learnset_game[p["id"]] = version_groups[latest]["identifier"]
    report["6세대 습득 기술 기록이 없어 기본 폼의 기술을 쓴 폼"] = borrowed
    report["6세대 폼인데 6세대 습득 기술이 없는 폼"] = [p["identifier"] for p in pokemon.values()
                                           if p["selectable"] and learnset_game[p["id"]] != gen6_game]

    # 진화 전 단계의 기술: 진화 전 폼이 배우는 기술(레벨업·기술머신·알 기술 등)은 진화한 뒤에도 쓸 수 있다.
    # 6세대 습득 기술끼리만 합친다. 예: 리자몽은 파이리의 알 기술인 용의춤·배북도 배운다.
    parents = defaultdict(set)
    for a, b in evolutions:
        parents[b].add(a)

    def ancestors(pid):
        found, stack = set(), [pid]
        while stack:
            for parent in parents[stack.pop()]:
                if parent not in found:
                    found.add(parent)
                    stack.append(parent)
        return found

    gen6_learnsets = {pid for pid, game in learnset_game.items() if game == gen6_game}
    inherited = {pid: set().union(*(learnsets[a] for a in ancestors(pid) if a in gen6_learnsets)) - learnsets[pid]
                 for pid in gen6_learnsets}
    for pid, extra in inherited.items():
        learnsets[pid] |= extra
    report["진화 전 단계에서 물려받은 습득 기술 수"] = sum(len(extra) for extra in inherited.values())
    # 전투 전용 폼은 원래 폼의 기술을 그대로 쓴다. 원래 폼도 전투 전용이면(테라파고스) 그 폼이 먼저 정해져야 한다.
    pending = list(form_changes)
    while pending:
        ready = [fc for fc in pending if fc[1] in learnsets]
        if not ready:
            break
        for pid, base_id, *_ in ready:
            learnsets.setdefault(pid, set()).update(learnsets[base_id])
            learnset_game.setdefault(pid, learnset_game[base_id])
            pending.remove((pid, base_id, *_))
    for p in pokemon.values():
        check(p["id"] in learnsets, f"{p['identifier']}의 습득 기술을 정하지 못했습니다.")
    report["습득 기술 4개 미만 (팀 선택 시 안내)"] = sorted(
        f"{by_id[pid]['identifier']}({len(m)})" for pid, m in learnsets.items()
        if len(m) < 4 and by_id[pid]["selectable"])
    games_used = defaultdict(int)
    for pid, g in learnset_game.items():
        if by_id[pid]["selectable"]:
            games_used[g] += 1
    report["습득 기술 기준 게임별 폼 수"] = dict(sorted(games_used.items(), key=lambda x: -x[1]))
    for pid, m in learnsets.items():
        check(all(moves[x][11] == "normal" for x in m), f"{by_id[pid]['identifier']}이 Z기술·다이맥스기를 배웁니다.")

    # ── 특성 ──────────────────────────────────────────────────
    ability_names, ability_fallback = localized(src("ability_names"), "ability_id")
    used_abilities = {a[1] for p in pokemon.values() for a in p["abilities"]}
    abilities = [(int(r["id"]), r["identifier"], ability_names[r["id"]], int(r["generation_id"]))
                 for r in src("abilities") if int(r["id"]) in used_abilities]
    check(len(abilities) == len(used_abilities), "포켓몬이 가진 특성 중 특성 목록에 없는 것이 있습니다.")
    report["한국어 이름이 없어 영어로 쓴 특성"] = [r[1] for r in abilities if str(r[0]) in ability_fallback]

    # ── 도구 ──────────────────────────────────────────────────
    item_names, item_fallback = localized(src("item_names"), "item_id")
    flag_names = {r["id"]: r["identifier"] for r in src("item_flags")}
    item_flags = defaultdict(set)
    for r in src("item_flag_map"):
        item_flags[r["item_id"]].add(flag_names[r["item_flag_id"]])
    needed_items = {fc[3] for fc in form_changes if fc[3] is not None}
    items = []
    for r in items_rows:
        holdable = bool(item_flags[r["id"]] & {"holdable", "holdable-active", "holdable-passive"})
        if holdable or categories[r["category_id"]] in ITEM_CATEGORIES or int(r["id"]) in needed_items:
            check(r["id"] in item_names, f"도구 이름이 없습니다: {r['identifier']}")
            items.append((int(r["id"]), r["identifier"], item_names[r["id"]], categories[r["category_id"]],
                          int(r["fling_power"]) if r["fling_power"] else None, int(holdable)))
    report["한국어 이름이 없어 영어로 쓴 도구"] = [i[1] for i in items if str(i[0]) in item_fallback]

    # ── 기술 부가 효과 (move_meta) ─────────────────────────────
    meta_categories = {r["id"]: r["identifier"] for r in src("move_meta_categories")}
    ailment_names = {r["id"]: r["identifier"] for r in src("move_meta_ailments")}
    target_names = {r["id"]: r["identifier"] for r in src("move_targets")}
    stat_names = {r["id"]: r["identifier"] for r in src("stats")}
    move_targets = {int(r["id"]): target_names[r["target_id"]] for r in moves_rows if int(r["id"]) in moves}
    optional = lambda value: int(value) if value else None
    move_meta = {}
    for r in src("move_meta"):
        mid = int(r["move_id"])
        if mid not in moves:
            continue
        ailment = ailment_names[r["meta_ailment_id"]]
        move_meta[mid] = (mid, meta_categories[r["meta_category_id"]], move_targets[mid],
                          None if ailment in ("none", "unknown") else ailment, int(r["ailment_chance"]),
                          optional(r["min_hits"]), optional(r["max_hits"]),
                          optional(r["min_turns"]), optional(r["max_turns"]),
                          int(r["drain"]), int(r["healing"]), int(r["crit_rate"]),
                          int(r["flinch_chance"]), int(r["stat_chance"]))

    # 능력치 변화의 대상: 공격 후 자신의 능력치가 바뀌는 기술(damage-raise, 예: 인파이트)은 자신,
    # 공격 후 상대 능력치를 내리는 기술(damage-lower)과 뽐내기류(swagger)는 상대,
    # 나머지는 기술의 대상이 자신·우리 편이면 자신, 아니면 상대다.
    stat_changes = []
    for r in src("move_meta_stat_changes"):
        mid = int(r["move_id"])
        if mid not in moves:
            continue
        stat = stat_names[r["stat_id"]]
        check(stat in STAT_IDENTIFIERS, f"알 수 없는 능력치 변화: 기술 {mid}, {stat}")
        meta = move_meta.get(mid)
        category = meta[1] if meta else None
        if category == "damage-raise":
            target = "self"
        elif category in ("damage-lower", "swagger"):
            target = "opponent"
        else:
            target = "self" if move_targets[mid] in SELF_TARGETS else "opponent"
        stat_changes.append((mid, stat, int(r["change"]), (meta[13] if meta else 0) or 100, target))

    # ── 기술 플래그 (move_flag_map) ───────────────────────────
    flag_identifiers = {r["id"]: r["identifier"] for r in src("move_flags")}
    move_flags = sorted({(int(r["move_id"]), flag_identifiers[r["move_flag_id"]]) for r in src("move_flag_map")
                         if int(r["move_id"]) in moves})

    # ── 프로젝트 전용 데이터 ──────────────────────────────────
    statuses = [(int(r["status_id"]), r["code"], r["name"]) for r in read_csv(CUSTOM_DIR / "status_conditions.csv")]
    status_effects = [(int(r["move_id"]), int(r["status_id"]), float(r["chance"]), r["target"], r["timing"],
                       int(r["effect_group"])) for r in read_csv(CUSTOM_DIR / "move_status_effects.csv")]
    check(all(e[0] in moves for e in status_effects), "상태이상 효과의 기술 번호가 기술 목록에 없습니다.")

    # 기술 1~559번의 상태이상은 위 CSV에서 직접 정리한 값을 쓰고, 560번 이후는 move_meta의 상태이상으로 채운다.
    # 이 게임의 상태이상(마비~헤롱헤롱)만 넣으며, 상대에게 적중했을 때(on_hit) 거는 것으로 본다.
    status_ids = {code: status_id for status_id, code, _ in statuses}
    custom_moves = {e[0] for e in status_effects}
    auto_status = []
    for mid, meta in sorted(move_meta.items()):
        if mid < AUTO_STATUS_FROM or mid in custom_moves or mid in CONDITIONAL_AILMENT_MOVES:
            continue
        if meta[3] in status_ids:
            status_effects.append((mid, status_ids[meta[3]], float(meta[4] or 100), "opponent", "on_hit", 0))
            auto_status.append(f"{moves[mid][1]}({meta[3]} {meta[4] or 100}%)")
    report["move_meta로 채운 상태이상 효과 (560번 이후)"] = auto_status

    # ── 저장 ──────────────────────────────────────────────────
    if out.exists() and not force:
        raise BuildError(f"{out}이 이미 있습니다. 덮어쓰려면 --force를 주세요.")
    temp = out.with_suffix(".building")
    if temp.exists():
        temp.unlink()
    db = sqlite3.connect(temp)
    try:
        db.execute("PRAGMA foreign_keys = ON")
        db.executescript(SCHEMA)
        db.executemany("INSERT INTO Type VALUES (?, ?, ?)", types)
        db.executemany("INSERT INTO TypeEffectiveness VALUES (?, ?, ?)", effectiveness)
        db.executemany("INSERT INTO PokemonSpecies VALUES (?, ?, ?, ?, ?, ?, ?, ?)", [
            (int(r["id"]), r["identifier"], species_names[r["id"]], int(r["generation_id"]), int(r["gender_rate"]),
             int(r["is_baby"]), int(r["is_legendary"]), int(r["is_mythical"]))
            for r in sorted(species_rows, key=lambda r: int(r["id"]))])
        db.executemany("INSERT INTO PokemonMove VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", sorted(moves.values()))
        db.executemany("INSERT INTO Item VALUES (?, ?, ?, ?, ?, ?)", sorted(items))
        db.executemany("INSERT INTO Ability VALUES (?, ?, ?, ?)", sorted(abilities))
        db.executemany(f"INSERT INTO Pokemon VALUES ({", ".join("?" * 21)})", [
            (p["id"], p["identifier"], p["species_id"], p["name"], p["form_name"], p["kind"], p["region"],
             p["is_default"], p["battle_only"], p["selectable"], p["type1"], p["type2"], *p["stats"],
             p["height"], p["weight"], learnset_game[p["id"]])
            for p in sorted(pokemon.values(), key=lambda p: p["id"])])
        db.executemany("INSERT INTO PokemonAbility VALUES (?, ?, ?, ?)", sorted(
            (p["id"], slot, ability_id, hidden) for p in pokemon.values() for slot, ability_id, hidden in p["abilities"]))
        db.executemany("INSERT INTO PokemonEvolution VALUES (?, ?)", sorted(evolutions))
        db.executemany("INSERT INTO PokemonFormChange VALUES (?, ?, ?, ?, ?)", sorted(form_changes))
        db.executemany("INSERT INTO PokemonLearnset VALUES (?, ?)", sorted(
            (pid, m) for pid, ms in learnsets.items() for m in ms))
        db.executemany("INSERT INTO StatusCondition VALUES (?, ?, ?)", statuses)
        db.executemany("INSERT INTO MoveStatusEffect VALUES (?, ?, ?, ?, ?, ?)", status_effects)
        db.executemany(f"INSERT INTO MoveMeta VALUES ({", ".join("?" * 14)})", sorted(move_meta.values()))
        db.executemany("INSERT INTO MoveStatChange VALUES (?, ?, ?, ?, ?)", sorted(stat_changes))
        db.executemany("INSERT INTO MoveFlag VALUES (?, ?)", move_flags)
        db.commit()
        check(db.execute("PRAGMA integrity_check").fetchone()[0] == "ok", "SQLite 무결성 검사 실패")
        check(not db.execute("PRAGMA foreign_key_check").fetchall(), "외래 키 검사 실패")
        counts = {t: db.execute(f"SELECT COUNT(*) FROM {t}").fetchone()[0] for t in (
            "PokemonSpecies", "Pokemon", "PokemonEvolution", "PokemonFormChange", "Ability", "PokemonAbility",
            "Item", "PokemonMove", "PokemonLearnset", "Type", "TypeEffectiveness", "StatusCondition",
            "MoveStatusEffect", "MoveMeta", "MoveStatChange", "MoveFlag")}
        counts["팀에 넣을 수 있는 폼"] = db.execute("SELECT COUNT(*) FROM Pokemon WHERE is_selectable = 1").fetchone()[0]
        counts["폼 종류별"] = dict(db.execute("SELECT form_kind, COUNT(*) FROM Pokemon GROUP BY form_kind").fetchall())
    finally:
        db.close()
    temp.replace(out)

    manifest = {
        "pokeapi_commit": POKEAPI_COMMIT,
        "source_files": {f"{name}.csv": hashlib.sha256((source / f"{name}.csv").read_bytes()).hexdigest()
                         for name in SOURCE_FILES},
        "custom_files": {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(CUSTOM_DIR.glob("*.csv"))},
        "counts": counts,
        "notes": report,
    }
    (out.parent / "source_manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return manifest


def main():
    parser = argparse.ArgumentParser(description="PokéAPI CSV로 Pokemon.db를 만든다.")
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--source", type=Path, help="PokéAPI CSV가 있는 폴더")
    group.add_argument("--download", type=Path, help="고정 커밋의 CSV를 이 폴더에 내려받은 뒤 사용")
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT)
    parser.add_argument("--force", action="store_true", help="기존 DB를 덮어쓴다")
    args = parser.parse_args()
    source = args.source or args.download
    try:
        if args.download:
            download(args.download)
        manifest = build(source, args.out, args.force)
    except BuildError as error:
        print(f"DB 생성 실패: {error}", file=sys.stderr)
        sys.exit(1)
    print(json.dumps(manifest["counts"], ensure_ascii=False, indent=2))
    for title, values in manifest["notes"].items():
        print(f"- {title}: {values if values else '없음'}")
    print(f"저장: {args.out}")


if __name__ == "__main__":
    main()
