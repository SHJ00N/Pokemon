// 랭크를 바꿀 수 있는 능력치. Stages 배열의 순서와 같다.
enum Stat { Attack, Defense, SpAttack, SpDefense, Speed, Accuracy, Evasion }

// 전투 중인 포켓몬. 레벨 50, 개체값 31(6V), 노력치 0, 성격 보정 없음으로 실제 능력치를 계산한다.
sealed class BattlePokemon
{
    public const int Level = 50;

    public PokemonInfo Info { get; }
    public PokemonMoveInfo[] Moves { get; }
    public int MaxHp { get; }
    public int Hp { get; set; }
    // 파워셰어·가드셰어로 바뀔 수 있고, 교체하면 원래 값으로 돌아온다.
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int SpAttack { get; set; }
    public int SpDefense { get; set; }
    public int Speed { get; }

    // 교체해도 남는 상태이상. StatusCondition.code(paralysis, sleep, freeze, burn, poison, bad-poison) 또는 null
    public string? Status { get; set; }
    public int SleepTurns { get; set; }     // 행동하려 할 때마다 1씩 줄고, 0이 되면 깨어나 행동한다.
    public int SleepStart { get; set; }     // 처음 잠든 값. 5세대는 교체하면 이 값으로 되돌아간다.
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
    public int ProtectStreak { get; set; }  // 방어·판별·버티기·패스트가드를 연속으로 성공한 횟수
    public bool UsedProtectThisTurn { get; set; }
    public int ChargeTurns { get; set; }    // 충전: 0보다 크면 전기 기술 위력 2배. 턴이 끝날 때마다 1씩 준다.
    public int ConfusionTurns { get; set; } // 0이면 혼란 아님. 행동하려 할 때마다 1씩 줄고 0이 되면 풀린다.
    public bool Infatuated { get; set; }    // 헤롱헤롱. 상대 포켓몬이 교체되거나 쓰러져도 풀린다.
    public int DrowsyTurns { get; set; }    // 하품. 턴이 끝날 때마다 1씩 줄고 0이 되면 잠든다.
    public int? LockedSlot { get; set; }    // 난동 기술로 고정된 기술 슬롯
    public int LockTurnsLeft { get; set; }

    private readonly int attack, defense, spAttack, spDefense;

    public BattlePokemon(PokemonInfo info, PokemonMoveInfo[] moves)
    {
        Info = info;
        Moves = moves;
        MaxHp = Hp = (2 * info.BaseHp + 31) * Level / 100 + Level + 10;
        Attack = attack = CalculateStat(info.BaseAttack);
        Defense = defense = CalculateStat(info.BaseDefense);
        SpAttack = spAttack = CalculateStat(info.BaseSpAttack);
        SpDefense = spDefense = CalculateStat(info.BaseSpDefense);
        Speed = CalculateStat(info.BaseSpeed);
    }

    public int GetStage(Stat stat) => Stages[(int)stat];

    public void OnSwitchOut()
    {
        Array.Clear(Stages);
        FocusEnergy = false;
        StockpileCount = StockpileDefense = StockpileSpDefense = 0;
        ChargeTurns = 0;
        AquaRing = Ingrained = Roosting = false;
        SubstituteHp = 0;
        Protected = Enduring = MagicCoat = UsedProtectThisTurn = false;
        ProtectStreak = 0;
        (Attack, Defense, SpAttack, SpDefense) = (attack, defense, spAttack, spDefense);
        ConfusionTurns = 0;
        Infatuated = false;
        DrowsyTurns = 0;
        LockedSlot = null;
        LockTurnsLeft = 0;
        if (Status == "sleep")
            SleepTurns = SleepStart;
    }

    public void OnSwitchIn() => ToxicCounter = 0;

    private static int CalculateStat(int baseStat) => (2 * baseStat + 31) * Level / 100 + 5;
}
