# 녹화 비교용 대본. 명령 형식은 replay.py의 Player 설명 참고.
# 각 대본은 끝까지 결정적으로 진행되도록 (시드 7, 행동 제한시간 3초) 짰고, 처음 녹화할 때 의도한 장면이 나오는지
# expect 문구로 확인한다("h:" 방장, "g:" 참가자가 받은 줄에 들어 있어야 하는 문구).
import os, sqlite3

DB = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
                  "Server", "resources", "db", "Pokemon.db")
_con = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)


def mon(pokemon_id, *moves):
    """포켓몬 한 마리. 준 기술을 앞에 두고, 모자라면 배우는 기술을 번호 순으로 채워 최대 4개."""
    learnset = [m for (m,) in _con.execute(
        "SELECT move_id FROM PokemonLearnset WHERE pokemon_id=? ORDER BY move_id", (pokemon_id,))]
    for move in moves:
        if move not in learnset:
            raise ValueError(f"{pokemon_id}번은 기술 {move}를 배우지 않는다")
    chosen = list(moves) + [m for m in learnset if m not in moves]
    return {"PokemonId": pokemon_id, "MoveIds": chosen[:min(4, len(learnset))]}


# 포켓몬
CHARIZARD, DITTO, MAGIKARP, SNORLAX, MEWTWO, RAYQUAZA = 6, 132, 129, 143, 150, 384
CROBAT, SCYTHER, SLAKING, WOBBUFFET, WAILORD, GYARADOS = 169, 123, 289, 202, 321, 130
BLISSEY, CHANSEY, DRIFBLIM, VENUSAUR, FORRETRESS, FEEBAS, SURSKIT = 242, 113, 426, 3, 205, 349, 283
# 기술
SWORDS_DANCE, FLAMETHROWER, EARTHQUAKE, FLY, PSYCHIC, CALM_MIND = 14, 53, 89, 19, 94, 347
U_TURN, PURSUIT, BATON_PASS, SPLASH, AMNESIA = 369, 228, 226, 150, 133
SOLAR_BEAM, HYPER_BEAM, OUTRAGE = 76, 63, 200
RAIN_DANCE, THUNDER, TRICK_ROOM, GRAVITY, GRASSY_TERRAIN = 240, 87, 433, 356, 580
STEALTH_ROCK, SPIKES, TOXIC_SPIKES, RAPID_SPIN = 446, 191, 390, 229
TRANSFORM, TAUNT, DISABLE = 144, 269, 50
MEGA_CHARIZARD_Y = 10035

SPLASHERS = [mon(WOBBUFFET, SPLASH), mon(WAILORD, SPLASH), mon(GYARADOS, SPLASH)]
WEAKLINGS = [mon(MAGIKARP), mon(FEEBAS), mon(SURSKIT)]

SCENARIOS = [
    dict(name="mega_xy",
         host_team=[mon(CHARIZARD, SWORDS_DANCE, FLAMETHROWER, EARTHQUAKE, FLY), mon(SNORLAX, AMNESIA), mon(MAGIKARP)],
         guest_team=[mon(MEWTWO, PSYCHIC, CALM_MIND), mon(SNORLAX), mon(WOBBUFFET)],
         # 메가 Y 선택 → 교체로 나갔다 돌아와도 메가 폼 → 두 번째 메가진화는 거절
         host=[f"USE_MOVE 1 MEGA {MEGA_CHARIZARD_Y}", "SWITCH 2", "SWITCH 1",
               "EXPECT_ERROR USE_MOVE 1 MEGA", "USE_MOVE 2", "SURRENDER"],
         guest=["USE_MOVE 1", "USE_MOVE 2", "USE_MOVE 2", "USE_MOVE 2", "WAIT"],
         expect=["h:메가리자몽Y로 메가진화했다", "g:메가리자몽Y로 메가진화했다", "h:ERROR"]),

    dict(name="uturn_baton",
         host_team=[mon(CROBAT, U_TURN, PURSUIT), mon(SCYTHER, SWORDS_DANCE, BATON_PASS), mon(SNORLAX, AMNESIA)],
         guest_team=[mon(SLAKING, PURSUIT, AMNESIA), mon(WOBBUFFET, SPLASH), mon(WAILORD, SPLASH)],
         # 유턴으로 턴 도중 교체 → 칼춤 → 배턴터치로 잠만보에게 넘김 → 교체하는 쪽을 따라가때리기
         host=["USE_MOVE 1", "SWITCH 2", "USE_MOVE 1", "USE_MOVE 2", "SWITCH 3", "SWITCH 1", "SURRENDER"],
         guest=["USE_MOVE 2", "USE_MOVE 2", "USE_MOVE 2", "USE_MOVE 1", "WAIT"],
         expect=["h:CHOOSE_SWITCH_MIDTURN", "h:배턴터치", "h:따라가때리기"]),

    dict(name="two_turn_moves",
         host_team=[mon(RAYQUAZA, SOLAR_BEAM, FLY, HYPER_BEAM, OUTRAGE), mon(SNORLAX), mon(MAGIKARP)],
         guest_team=[mon(BLISSEY), mon(CHANSEY), mon(WOBBUFFET, SPLASH)],
         # 솔라빔·공중날기(2턴), 파괴광선(다음 턴 쉼), 역린(난동). 묶인 턴에는 안내가 LOCKED라 명령을 쓰지 않는다.
         host=["USE_MOVE 1", "USE_MOVE 2", "USE_MOVE 3", "USE_MOVE 4", "SURRENDER"],
         guest=["AUTO*"],
         expect=["h:LOCKED", "h:솔라빔", "h:공중날기", "h:파괴광선", "h:역린"]),

    dict(name="weather_field",
         host_team=[mon(SNORLAX, RAIN_DANCE, THUNDER), mon(MEWTWO, TRICK_ROOM, GRAVITY), mon(VENUSAUR, GRASSY_TERRAIN)],
         guest_team=[mon(DRIFBLIM, FLY), mon(MAGIKARP), mon(WOBBUFFET, SPLASH)],
         # 비바라기+번개, 트릭룸, 중력(공중날기 선택 거절), 그래스필드
         host=["USE_MOVE 1", "USE_MOVE 2", "SWITCH 2", "USE_MOVE 2", "USE_MOVE 1", "SWITCH 3", "USE_MOVE 1", "SURRENDER"],
         guest=["USE_MOVE 1", "USE_MOVE 2", "USE_MOVE 2", "EXPECT_ERROR USE_MOVE 1", "USE_MOVE 2", "USE_MOVE 2",
                "USE_MOVE 2", "WAIT"],
         expect=["h:비", "h:트릭룸", "h:중력", "g:ERROR 중력", "h:그래스필드"]),

    dict(name="hazards",
         host_team=[mon(FORRETRESS, STEALTH_ROCK, SPIKES, TOXIC_SPIKES, RAPID_SPIN), mon(SNORLAX), mon(MAGIKARP)],
         guest_team=SPLASHERS,
         # 스텔스록·압정뿌리기·독압정을 깔고, 참가자가 교체해 들어오며 맞는다. 고속스핀.
         host=["USE_MOVE 1", "USE_MOVE 2", "USE_MOVE 3", "USE_MOVE 4", "SURRENDER"],
         guest=["USE_MOVE 1", "SWITCH 2", "SWITCH 3", "SWITCH 1", "WAIT"],
         expect=["h:스텔스록", "h:압정뿌리기", "h:독압정", "h:고속스핀"]),

    dict(name="transform_struggle",
         host_team=[mon(DITTO), mon(SNORLAX, AMNESIA), mon(MAGIKARP)],
         guest_team=[mon(MEWTWO, TAUNT, CALM_MIND), mon(WOBBUFFET, SPLASH), mon(WAILORD, SPLASH)],
         # 변신 → 복사한 기술 사용 → 교체로 변신이 풀림 → 도발에 걸린 메타몽은 변신을 못 써서 발버둥
         host=["USE_MOVE 1", "USE_MOVE 2", "SWITCH 2", "SWITCH 1", "USE_MOVE 1", "SURRENDER"],
         guest=["USE_MOVE 2", "USE_MOVE 2", "USE_MOVE 2", "USE_MOVE 1", "USE_MOVE 2", "WAIT"],
         expect=["h:변신", "h:도발", "h:발버둥"]),

    dict(name="faint_and_replace",
         host_team=[mon(MEWTWO, PSYCHIC), mon(SNORLAX), mon(MAGIKARP)],
         guest_team=WEAKLINGS,
         # 쓰러지면 CHOOSE_SWITCH / WAIT_SWITCH, 끝까지 싸워 BATTLE_END
         host=["AUTO*"],
         guest=["AUTO*"],
         expect=["g:CHOOSE_SWITCH", "h:WAIT_SWITCH", "h:BATTLE_END WIN", "g:BATTLE_END LOSE"]),

    dict(name="rematch_resets_field",
         host_team=[mon(SNORLAX, RAIN_DANCE), mon(MAGIKARP), mon(WOBBUFFET)],
         guest_team=SPLASHERS,
         # 비가 오는 중에 항복 → 양쪽 재도전(방장 먼저) → 두 번째 전투는 날씨 없이 시작
         host=["USE_MOVE 1", "SURRENDER", "REMATCH", "SURRENDER"],
         guest=["USE_MOVE 1", "WAIT", "@PLAYER_REMATCH REMATCH", "WAIT"],
         expect=["h:REMATCH_REQUESTED", "g:PLAYER_REMATCH", "h:REMATCH_STARTED", "g:REMATCH_STARTED"]),

    dict(name="leave_mid_battle",
         host_team=[mon(SNORLAX, AMNESIA), mon(MAGIKARP), mon(WOBBUFFET)],
         guest_team=SPLASHERS,
         host=["USE_MOVE 1", "WAIT"],
         guest=["USE_MOVE 1", "DISCONNECT"],
         expect=["h:BATTLE_END WIN LEFT"]),

    dict(name="invalid_commands",
         host_team=[mon(CHARIZARD, SWORDS_DANCE, FLAMETHROWER, FLY, EARTHQUAKE), mon(MAGIKARP), mon(SNORLAX)],
         guest_team=[mon(MEWTWO, DISABLE, PSYCHIC, CALM_MIND), mon(WOBBUFFET, SPLASH), mon(WAILORD, SPLASH)],
         # 없는 메가 폼, 범위 밖 기술 번호, 두 번째 메가진화, 사슬묶기된 기술, 쓰러진 포켓몬으로 교체
         host=["EXPECT_ERROR USE_MOVE 1 MEGA 99999", "EXPECT_ERROR USE_MOVE 9", "USE_MOVE 1 MEGA",
               "EXPECT_ERROR USE_MOVE 1 MEGA", "USE_MOVE 1",
               "EXPECT_ERROR USE_MOVE 1", "SWITCH 2",
               "EXPECT_ERROR SWITCH 2", "SWITCH 3",
               "SURRENDER"],
         guest=["USE_MOVE 2", "USE_MOVE 1", "USE_MOVE 2", "WAIT"],
         expect=["h:ERROR 현재 포켓몬이 메가진화할 수 있는 폼을", "h:ERROR 지금은 메가진화할 수 없습니다",
                 "h:ERROR 사슬묶기", "h:ERROR 교체할 수 없는 포켓몬"]),

    dict(name="timeout_action",
         host_team=[mon(SNORLAX, AMNESIA), mon(MAGIKARP), mon(WOBBUFFET)],
         guest_team=SPLASHERS,
         host=["WAIT", "SURRENDER"],
         guest=["USE_MOVE 1", "WAIT"],
         expect=["h:시간 초과로", "g:상대가 시간 초과로"]),

    dict(name="timeout_forced_switch",
         host_team=[mon(MEWTWO, PSYCHIC), mon(SNORLAX), mon(MAGIKARP)],
         guest_team=WEAKLINGS,
         host=["USE_MOVE 1", "SURRENDER"],
         guest=["USE_MOVE 1", "WAIT", "WAIT"],
         expect=["g:CHOOSE_SWITCH", "g:시간 초과로 교체할 포켓몬을 자동 선택했다"]),

    dict(name="timeout_midturn_switch",
         host_team=[mon(CROBAT, U_TURN), mon(SNORLAX), mon(MAGIKARP)],
         guest_team=SPLASHERS,
         host=["USE_MOVE 1", "WAIT", "SURRENDER"],
         guest=["USE_MOVE 1", "WAIT"],
         expect=["h:CHOOSE_SWITCH_MIDTURN", "h:시간 초과로 교체할 포켓몬을 자동 선택했다"]),
]
