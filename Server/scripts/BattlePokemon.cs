// 랭크를 바꿀 수 있는 능력치. Stages 배열의 순서와 같다.
enum Stat { Attack, Defense, SpAttack, SpDefense, Speed, Accuracy, Evasion }

// 전투 중인 포켓몬. 레벨 50, 개체값 31(6V), 노력치 0, 성격 보정 없음으로 실제 능력치를 계산한다.
sealed class BattlePokemon
{
    public const int Level = 50;

    public PokemonInfo Info { get; }
    // 흉내내기·변신으로 바뀔 수 있고, 교체하면 원래 기술로 돌아온다(스케치는 원래 기술도 바꾼다).
    public PokemonMoveInfo[] Moves { get; }
    public int MaxHp { get; }
    public int Hp { get; set; }
    // 파워셰어·가드셰어·파워트릭·변신으로 바뀔 수 있고, 교체하면 원래 값으로 돌아온다.
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int SpAttack { get; set; }
    public int SpDefense { get; set; }
    public int Speed { get; set; }
    // 지금 타입. 물붓기·텍스처·미러타입·보호색·변신으로 바뀌고, 핼러윈·숲의저주는 세 번째 타입(AddedType)을 더한다.
    // 교체하면 원래 타입으로 돌아온다.
    public int[] BaseTypes { get; set; }
    public int? AddedType { get; set; }
    public IEnumerable<int> Types => AddedType is int added && !BaseTypes.Contains(added) ? BaseTypes.Append(added) : BaseTypes;

    // 교체해도 남는 상태이상. StatusCondition.code(paralysis, sleep, freeze, burn, poison, bad-poison) 또는 null
    public string? Status { get; set; }
    // 행동하려 할 때마다 1씩 줄고, 0이 되면 깨어나 행동한다. 6세대는 교체해도 남은 값이 그대로다.
    public int SleepTurns { get; set; }
    public int ToxicCounter { get; set; }   // 맹독 누적 횟수. 교체해서 나오면 처음부터 다시 센다.

    // 교체하면 풀리는 상태
    public int[] Stages { get; } = new int[7];  // 능력치 랭크 -6~+6 (Stat 순서)
    public bool FocusEnergy { get; set; }   // 기충전: 급소 랭크 +2
    public int StockpileCount { get; set; } // 비축하기 횟수 (최대 3)
    public int StockpileDefense { get; set; }   // 비축하기로 실제로 오른 방어·특수방어 랭크 (꿀꺽을 쓰면 되돌린다)
    public int StockpileSpDefense { get; set; }
    public bool AquaRing { get; set; }      // 아쿠아링: 턴이 끝날 때 최대 HP 1/16 회복
    public bool Ingrained { get; set; }     // 뿌리박기: 턴이 끝날 때 최대 HP 1/16 회복, 직접 교체할 수 없음
    public bool Roosting { get; set; }      // 날개쉬기를 쓴 턴: 비행 타입이 없어진다.
    public int SubstituteHp { get; set; }   // 대타출동으로 만든 대타의 남은 HP. 0이면 대타 없음
    // 이번 턴에만 유지되는 상태 (턴이 끝나면 풀린다)
    public bool Protected { get; set; }     // 방어·판별
    public bool Enduring { get; set; }      // 버티기
    public bool MagicCoat { get; set; }     // 매직코트
    public bool Flinched { get; set; }      // 풀죽음: 이번 턴에 아직 행동하지 않았다면 행동하지 못한다.
    public int MoveActions { get; set; }    // 나온 뒤 기술을 쓰려고 한 횟수. 속이기는 첫 번째일 때만 성공한다.
    public int ProtectStreak { get; set; }  // 방어·판별·버티기·패스트가드를 연속으로 성공한 횟수
    public bool UsedProtectThisTurn { get; set; }
    public int ChargeTurns { get; set; }    // 충전: 0보다 크면 전기 기술 위력 2배. 턴이 끝날 때마다 1씩 준다.
    public int ConfusionTurns { get; set; } // 0이면 혼란 아님. 행동하려 할 때마다 1씩 줄고 0이 되면 풀린다.
    public bool Infatuated { get; set; }    // 헤롱헤롱. 상대 포켓몬이 교체되거나 쓰러져도 풀린다.
    public int DrowsyTurns { get; set; }    // 하품. 턴이 끝날 때마다 1씩 줄고 0이 되면 잠든다.
    public int? LockedSlot { get; set; }    // 난동 기술로 고정된 기술 슬롯
    public int LockTurnsLeft { get; set; }
    public int? ChargingSlot { get; set; }  // 2턴 기술을 모으는 중인 기술 슬롯. 다음 턴에 자동으로 공격한다.
    public string? Hidden { get; set; }     // 공중날기·구멍파기 등으로 숨은 곳 (air, underground, underwater, vanished)
    public bool SkyDropped { get; set; }    // 상대의 프리폴에 붙잡혀 공중에 있음. 행동·교체할 수 없다.
    public int? RechargeSlot { get; set; }  // 파괴광선 등을 맞힌 기술 슬롯. 다음 턴은 반동으로 움직이지 못한다.
    public HashSet<int> UsedMoveIds { get; } = [];  // 나온 뒤 쓴 기술 (비장의무기 조건). 교체해서 나오면 처음부터 센다.
    public BattlePokemon? TrappedBy { get; set; }   // 검은눈빛·블록·거미집을 쓴 상대. 그 포켓몬이 나와 있는 동안 교체할 수 없다.
    public BattlePokemon? BoundBy { get; set; }     // 조이기 등 묶기 기술을 쓴 상대
    public int BoundTurns { get; set; }             // 턴이 끝날 때마다 1씩 줄고 0이 되면 풀린다.
    public string? BoundMove { get; set; }          // 묶은 기술 이름
    public int RolloutHits { get; set; }    // 구르기·아이스볼을 이어서 맞힌 횟수 (0~4)
    public bool DefenseCurled { get; set; } // 웅크리기를 썼음: 구르기·아이스볼 위력 2배
    public int FuryCutterHits { get; set; } // 연속자르기를 이어서 맞힌 횟수
    public int BideTurns { get; set; }      // 참기: 남은 참는 턴 (0이면 참기 중이 아님)
    public int BideDamage { get; set; }     // 참는 동안 받은 데미지
    public int MagnitudeLevel { get; set; } // 이번에 나온 매그니튜드 크기와 위력
    public int MagnitudePower { get; set; }
    public int PresentPower { get; set; }   // 이번에 나온 프레젠트 위력
    public PokemonMoveInfo? ChargingMove { get; set; }      // 모으는 중인 2턴 기술 (손가락흔들기 등으로 나온 기술 포함)
    public int? LastMoveSlot { get; set; }                  // 마지막으로 쓴 기술 슬롯 (사슬묶기·앙코르·트집)
    public PokemonMoveInfo? LastExecutedMove { get; set; }  // 마지막으로 실제로 나간 기술 (따라하기·흉내내기·텍스처2)
    // 기술 제한
    public int? DisabledSlot { get; set; }  // 사슬묶기로 쓸 수 없는 슬롯
    public int DisableTurns { get; set; }
    public int? EncoreSlot { get; set; }    // 앙코르로 계속 써야 하는 슬롯
    public int EncoreTurns { get; set; }
    public bool Tormented { get; set; }     // 트집: 같은 기술을 연속으로 쓸 수 없다.
    public int TauntTurns { get; set; }     // 도발: 변화 기술을 쓸 수 없다.
    public int HealBlockTurns { get; set; } // 회복봉인: 회복 기술을 쓸 수 없고 HP가 회복되지 않는다.
    public bool Imprisoning { get; set; }   // 봉인: 자신이 아는 기술을 상대가 쓸 수 없다.
    // 턴마다 효과가 있는 상태
    public bool LeechSeeded { get; set; }   // 씨뿌리기: 턴 끝에 1/8을 빼앗긴다.
    public bool Nightmare { get; set; }     // 악몽: 잠든 동안 턴 끝에 1/4 데미지
    public bool Cursed { get; set; }        // 저주(고스트): 턴 끝에 1/4 데미지
    public int PerishCount { get; set; }    // 멸망의노래: 0이 아니면 턴 끝마다 1씩 줄고 0이 되면 쓰러진다.
    public bool DestinyBond { get; set; }   // 길동무: 다음 행동 전까지 상대 공격에 쓰러지면 상대도 쓰러진다.
    public bool Raging { get; set; }        // 분노: 다른 기술을 쓰기 전까지 공격을 맞으면 공격 +1
    public int UproarTurns { get; set; }    // 소란피기: 남은 턴. 그동안 아무도 잠들 수 없다.
    // 명중·회피·땅
    public BattlePokemon? LockOnTarget { get; set; }    // 록온·마음의눈: 이 상대에게 다음 턴까지 반드시 맞는다.
    public int LockOnTurns { get; set; }
    public bool Identified { get; set; }    // 꿰뚫어보기·냄새구별: 회피율 상승 무시, 노말·격투 기술이 고스트에게 맞는다.
    public bool MiracleEye { get; set; }    // 미라클아이: 회피율 상승 무시, 에스퍼 기술이 악에게 맞는다.
    public int TelekinesisTurns { get; set; }   // 텔레키네시스: 떠올라 반드시 맞고 땅 기술은 맞지 않는다.
    public int MagnetRiseTurns { get; set; }    // 전자부유: 땅 기술을 맞지 않는다.
    public bool SmackedDown { get; set; }   // 떨어뜨리기: 땅에 떨어져 땅 기술을 맞는다.
    public bool Transformed { get; set; }   // 변신
    public bool PowerTricked { get; set; }  // 파워트릭: 공격과 방어가 바뀌어 있다.
    // 이번 턴에만 유지되는 상태
    public string? ProtectKind { get; set; }    // 킹실드(kings)·니들가드(spiky)로 막는 중
    public bool Snatching { get; set; }     // 가로채기: 상대의 자신 대상 변화 기술을 가로챈다.
    public bool Powdered { get; set; }      // 분진: 불꽃 기술을 쓰면 폭발한다.
    public bool Electrified { get; set; }   // 송전: 이번 턴에 쓰는 기술이 전기 타입이 된다.
    // 이번 턴의 기록 (턴이 시작될 때 지운다)
    public bool MovedThisTurn { get; set; } // 이번 턴에 이미 행동했는지 (기습·보복)
    public bool HitThisTurn { get; set; }   // 이번 턴에 공격을 맞았는지 (힘껏펀치·리벤지·눈사태)
    public bool HurtThisTurn { get; set; }  // 이번 턴에 데미지를 입었는지 (승부굳히기)
    public int PhysicalDamageTaken { get; set; }    // 이번 턴에 마지막으로 맞은 물리·특수 공격의 데미지 (카운터·미러코트·메탈버스트)
    public int SpecialDamageTaken { get; set; }
    public bool SwitchingOut { get; set; }  // 교체하려는 중 (따라가때리기 위력 2배)

    public void ClearTurnRecord()
    {
        MovedThisTurn = HitThisTurn = HurtThisTurn = SwitchingOut = false;
        Snatching = Powdered = Electrified = false;
        PhysicalDamageTaken = SpecialDamageTaken = 0;
    }

    private readonly int attack, defense, spAttack, spDefense, speed;
    private readonly PokemonMoveInfo[] originalMoves;

    public BattlePokemon(PokemonInfo info, PokemonMoveInfo[] moves)
    {
        Info = info;
        Moves = moves;
        originalMoves = moves.ToArray();
        MaxHp = Hp = (2 * info.BaseHp + 31) * Level / 100 + Level + 10;
        Attack = attack = CalculateStat(info.BaseAttack);
        Defense = defense = CalculateStat(info.BaseDefense);
        SpAttack = spAttack = CalculateStat(info.BaseSpAttack);
        SpDefense = spDefense = CalculateStat(info.BaseSpDefense);
        Speed = speed = CalculateStat(info.BaseSpeed);
        BaseTypes = OriginalTypes;
    }

    private int[] OriginalTypes => Info.Type2Id is int type2 ? [Info.Type1Id, type2] : [Info.Type1Id];

    public int GetStage(Stat stat) => Stages[(int)stat];

    // 스케치: 이 슬롯의 기술을 교체해도 남게 바꾼다.
    public void LearnPermanently(int slot, PokemonMoveInfo move) => Moves[slot - 1] = originalMoves[slot - 1] = move;

    public void OnSwitchOut()
    {
        Array.Clear(Stages);
        FocusEnergy = false;
        StockpileCount = StockpileDefense = StockpileSpDefense = 0;
        ChargeTurns = 0;
        AquaRing = Ingrained = Roosting = false;
        SubstituteHp = 0;
        Protected = Enduring = MagicCoat = UsedProtectThisTurn = Flinched = false;
        ProtectStreak = 0;
        (Attack, Defense, SpAttack, SpDefense, Speed) = (attack, defense, spAttack, spDefense, speed);
        originalMoves.CopyTo(Moves, 0);
        BaseTypes = OriginalTypes;
        AddedType = null;
        ChargingMove = LastExecutedMove = null;
        LastMoveSlot = DisabledSlot = EncoreSlot = null;
        DisableTurns = EncoreTurns = TauntTurns = HealBlockTurns = PerishCount = UproarTurns = 0;
        Tormented = Imprisoning = LeechSeeded = Nightmare = Cursed = DestinyBond = Raging = false;
        LockOnTarget = null;
        LockOnTurns = TelekinesisTurns = MagnetRiseTurns = 0;
        Identified = MiracleEye = SmackedDown = Transformed = PowerTricked = false;
        ProtectKind = null;
        ConfusionTurns = 0;
        Infatuated = false;
        DrowsyTurns = 0;
        LockedSlot = null;
        LockTurnsLeft = 0;
        ChargingSlot = RechargeSlot = null;
        Hidden = null;
        SkyDropped = false;
        ClearTurnRecord();
        TrappedBy = BoundBy = null;
        BoundTurns = 0;
        BoundMove = null;
        RolloutHits = FuryCutterHits = BideTurns = BideDamage = 0;
        DefenseCurled = false;
    }

    public void OnSwitchIn()
    {
        ToxicCounter = 0;
        MoveActions = 0;
        UsedMoveIds.Clear();
    }

    private static int CalculateStat(int baseStat) => (2 * baseStat + 31) * Level / 100 + 5;
}
