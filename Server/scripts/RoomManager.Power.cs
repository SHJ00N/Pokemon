// 위력이 조건에 따라 바뀌거나 정해진 데미지를 주는 공격기. 수치는 6세대 규칙을 따른다.
partial class RoomManager
{
    // 조건이 맞으면 위력 2배
    private const int SmellingSaltsMoveId = 265, WakeUpSlapMoveId = 358, RevengeMoveId = 279, AvalancheMoveId = 419,
        BrineMoveId = 362, PaybackMoveId = 371, AssuranceMoveId = 372, RetaliateMoveId = 514, PursuitMoveId = 228,
        AcrobaticsMoveId = 512, FusionFlareMoveId = 558, FusionBoltMoveId = 559;
    // 위력이 바뀌는 기술
    private const int RolloutMoveId = 205, IceBallMoveId = 301, FuryCutterMoveId = 210, EchoedVoiceMoveId = 497,
        EruptionMoveId = 284, WaterSpoutMoveId = 323, StoredPowerMoveId = 500, LowKickMoveId = 67, GrassKnotMoveId = 447,
        HeavySlamMoveId = 484, HeatCrashMoveId = 535, FlailMoveId = 175, ReversalMoveId = 179, WringOutMoveId = 378,
        CrushGripMoveId = 462, GyroBallMoveId = 360, ElectroBallMoveId = 486, PresentMoveId = 217, MagnitudeMoveId = 222,
        ReturnMoveId = 216, FrustrationMoveId = 218, PunishmentMoveId = 386, SpitUpMoveId = 255, BeatUpMoveId = 251,
        DefenseCurlMoveId = 111, FlingMoveId = 374;
    // 데미지 계산이 다른 기술
    private const int FoulPlayMoveId = 492, FlyingPressMoveId = 560, FreezeDryMoveId = 573, SynchronoiseMoveId = 485,
        HiddenPowerMoveId = 237, SecretPowerMoveId = 290;
    // 사이코쇼크·사이코브레이크·신비의칼: 특수 기술이지만 상대의 방어로 계산한다.
    private static readonly HashSet<int> DefenseTargetMoveIds = [473, 540, 548];
    // 야금야금·성스러운칼: 상대의 방어·회피율 랭크를 무시한다.
    private static readonly HashSet<int> IgnoreStageMoveIds = [498, 533];
    // 정해진 데미지를 주는 기술
    private const int SonicBoomMoveId = 49, DragonRageMoveId = 82, SeismicTossMoveId = 69, NightShadeMoveId = 101,
        PsywaveMoveId = 149, SuperFangMoveId = 162, EndeavorMoveId = 283, FinalGambitMoveId = 515,
        CounterMoveId = 68, MirrorCoatMoveId = 243, MetalBurstMoveId = 368, BideMoveId = 117;
    // 일격필살: 가위자르기, 뿔드릴, 땅가르기, 절대영도
    private static readonly HashSet<int> OhkoMoveIds = [12, 32, 90, 329];

    // 기술의 위력. 조건에 따라 바뀌는 위력과 위력 값이 DB에 없는 기술의 위력을 계산한다. 위력이 없으면 null
    // hit: 연속기의 몇 번째 타격인지 (집단구타)
    private int? BasePower(GameRoom room, BattleSide attackerSide, BattleSide defenderSide, BattlePokemon attacker,
        PokemonMoveInfo move, int hit)
    {
        BattlePokemon defender = defenderSide.Current;
        int? power = move.Id switch
        {
            TrumpCardMoveId => TrumpCardPower,
            // 은혜갚기·화풀이: 친밀도가 없는 게임이라 각각 가장 강할 때의 위력 102로 쓴다.
            ReturnMoveId or FrustrationMoveId => 102,
            FlingMoveId => 45,      // 내던지기: 지닌 도구가 없는 게임이라 위력 45로 고정한다.
            // 안다리걸기·풀묶기: 상대가 무거울수록 (0.1kg 단위)
            LowKickMoveId or GrassKnotMoveId => defender.Info.Weight switch
            {
                < 100 => 20, < 250 => 40, < 500 => 60, < 1000 => 80, < 2000 => 100, _ => 120
            },
            // 헤비봄버·히트스탬프: 자신이 상대보다 무거울수록
            HeavySlamMoveId or HeatCrashMoveId => (attacker.Info.Weight / Math.Max(1, defender.Info.Weight)) switch
            {
                >= 5 => 120, 4 => 100, 3 => 80, 2 => 60, _ => 40
            },
            // 바둥바둥·기사회생: 자신의 HP가 적을수록
            FlailMoveId or ReversalMoveId => (48 * attacker.Hp / attacker.MaxHp) switch
            {
                <= 1 => 200, <= 4 => 150, <= 9 => 100, <= 16 => 80, <= 32 => 40, _ => 20
            },
            // 쥐어짜기·묵사발: 상대의 HP가 많을수록 (최대 120), 분화·해수스파우팅: 자신의 HP가 많을수록 (최대 150)
            WringOutMoveId or CrushGripMoveId => Math.Max(1, 120 * defender.Hp / defender.MaxHp),
            EruptionMoveId or WaterSpoutMoveId => Math.Max(1, 150 * attacker.Hp / attacker.MaxHp),
            // 자이로볼: 상대가 빠를수록 (최대 150), 일렉트릭볼: 자신이 상대보다 빠를수록
            GyroBallMoveId => Math.Min(150, 25 * GetSpeed(defenderSide) / Math.Max(1, GetSpeed(attackerSide)) + 1),
            ElectroBallMoveId => new[] { 40, 60, 80, 120, 150 }[Math.Min(4, GetSpeed(attackerSide) / Math.Max(1, GetSpeed(defenderSide)))],
            // 어시스트파워: 자신의 오른 랭크 1당 +20, 혼내기: 상대의 오른 랭크 1당 +20 (최대 200)
            StoredPowerMoveId => 20 + 20 * attacker.Stages.Where(stage => stage > 0).Sum(),
            PunishmentMoveId => Math.Min(200, 60 + 20 * defender.Stages.Where(stage => stage > 0).Sum()),
            SpitUpMoveId => 100 * attacker.StockpileCount,      // 토해내기: 비축 1개당 100
            MagnitudeMoveId => attacker.MagnitudePower,
            PresentMoveId => attacker.PresentPower,
            // 구르기·아이스볼: 맞힐 때마다 2배 (웅크리기를 썼으면 다시 2배), 연속자르기: 맞힐 때마다 2배 (최대 160)
            RolloutMoveId or IceBallMoveId => (30 << attacker.RolloutHits) * (attacker.DefenseCurled ? 2 : 1),
            FuryCutterMoveId => 40 << Math.Min(2, attacker.FuryCutterHits),
            EchoedVoiceMoveId => 40 * room.EchoedVoiceCount,    // 에코보이스: 매 턴 이어서 쓰이면 40씩 (최대 200)
            BeatUpMoveId => 5 + BeatUpMembers(attackerSide)[hit - 1].Info.BaseAttack / 10,
            _ => move.Power
        };
        if (power is not int value)
            return null;

        bool doubled = move.Id switch
        {
            FacadeMoveId => attacker.Status is Burn or Poison or BadPoison or Paralysis,
            HexMoveId => defender.Status != null,
            VenoshockMoveId => defender.Status is Poison or BadPoison,
            SmellingSaltsMoveId => defender.Status == Paralysis,
            WakeUpSlapMoveId => defender.Status == Sleep,
            RevengeMoveId or AvalancheMoveId => attacker.HitThisTurn,       // 이번 턴에 먼저 맞았으면
            BrineMoveId => defender.Hp * 2 <= defender.MaxHp,               // 상대 HP가 절반 이하
            PaybackMoveId => defender.MovedThisTurn,                        // 상대가 먼저 행동했으면
            AssuranceMoveId => defender.HurtThisTurn,                       // 상대가 이번 턴에 이미 데미지를 입었으면
            RetaliateMoveId => attackerSide.FaintedLastTurn,                // 지난 턴에 우리 편이 쓰러졌으면
            PursuitMoveId => defender.SwitchingOut,                         // 상대가 교체하려고 하면
            AcrobaticsMoveId => true,                                       // 지닌 도구가 없으면 (이 게임은 항상)
            FusionFlareMoveId => room.LastMoveIdThisTurn == FusionBoltMoveId, // 이번 턴 바로 앞에 크로스썬더가 나왔으면
            FusionBoltMoveId => room.LastMoveIdThisTurn == FusionFlareMoveId,
            _ => false
        };
        return doubled ? value * 2 : value;
    }

    // 정해진 데미지를 주는 기술의 데미지. 아니면 null. 급소·자속·난수·상성 배율 없이 효과가 없는지만 본다.
    private static int? FixedDamage(BattlePokemon attacker, BattlePokemon defender, PokemonMoveInfo move) => move.Id switch
    {
        SonicBoomMoveId => 20,
        DragonRageMoveId => 40,
        SeismicTossMoveId or NightShadeMoveId => BattlePokemon.Level,
        PsywaveMoveId => Math.Max(1, BattlePokemon.Level * Random.Shared.Next(50, 151) / 100),
        SuperFangMoveId => Math.Max(1, defender.Hp / 2),                // 분노의앞니: 상대 HP의 절반
        EndeavorMoveId => defender.Hp - attacker.Hp,                    // 죽기살기: 상대 HP를 자신의 HP만큼으로
        FinalGambitMoveId => attacker.Hp,                               // 목숨걸기: 자신의 HP만큼 (자신은 쓰러짐)
        CounterMoveId => attacker.PhysicalDamageTaken * 2,              // 카운터: 이번 턴에 받은 물리 데미지 2배
        MirrorCoatMoveId => attacker.SpecialDamageTaken * 2,            // 미러코트: 이번 턴에 받은 특수 데미지 2배
        MetalBurstMoveId => Math.Max(attacker.PhysicalDamageTaken, attacker.SpecialDamageTaken) * 3 / 2,
        BideMoveId => attacker.BideDamage * 2,                          // 참기: 참는 동안 받은 데미지 2배
        _ when OhkoMoveIds.Contains(move.Id) => defender.Hp,
        _ => null
    };

    // 위력·데미지를 계산하기 전에 실패하는 경우
    private static bool PowerMoveFails(BattleSide attackerSide, BattlePokemon attacker, BattlePokemon defender,
        PokemonMoveInfo move) => move.Id switch
    {
        EndeavorMoveId => defender.Hp <= attacker.Hp,
        CounterMoveId => attacker.PhysicalDamageTaken == 0,
        MirrorCoatMoveId => attacker.SpecialDamageTaken == 0,
        MetalBurstMoveId => attacker.PhysicalDamageTaken == 0 && attacker.SpecialDamageTaken == 0,
        SpitUpMoveId => attacker.StockpileCount == 0,
        BeatUpMoveId => BeatUpMembers(attackerSide).Length == 0,
        _ => false
    };

    // 집단구타에 참여하는 포켓몬: 쓰러지지 않고 상태이상이 없는 우리 팀 포켓몬 (자신 포함). 한 마리당 한 번 때린다.
    private static BattlePokemon[] BeatUpMembers(BattleSide side) =>
        side.Team.Where(pokemon => pokemon.Hp > 0 && pokemon.Status == null).ToArray();

    // 매그니튜드: 4(5%) 10, 5(10%) 30, 6(20%) 50, 7(30%) 70, 8(20%) 90, 9(10%) 110, 10(5%) 150
    private static void RollMagnitude(BattlePokemon user)
    {
        int roll = Random.Shared.Next(100);
        (user.MagnitudeLevel, user.MagnitudePower) = roll switch
        {
            < 5 => (4, 10), < 15 => (5, 30), < 35 => (6, 50), < 65 => (7, 70), < 85 => (8, 90), < 95 => (9, 110), _ => (10, 150)
        };
    }

    // 프레젠트: 40% 위력 40, 30% 위력 80, 10% 위력 120, 20%는 상대의 HP를 최대 HP의 1/4 회복시킨다.
    // 회복이면 처리하고 false를 돌려준다.
    private bool RollPresent(GameRoom room, BattleSide attacker, BattleSide defender, PokemonMoveInfo move)
    {
        BattlePokemon user = attacker.Current, target = defender.Current;
        int roll = Random.Shared.Next(100);
        user.PresentPower = roll switch { < 40 => 40, < 70 => 80, < 80 => 120, _ => 0 };
        if (user.PresentPower > 0)
            return true;
        LogPokemon(room, attacker, $"{user.Info.Name}의 {move.Name}!");
        if (!AccuracyCheck(room, user, target, move))
            LogBoth(room, attacker, "하지만 빗나갔다!");
        else if (!Heal(room, defender, target.MaxHp / 4))
            LogBoth(room, attacker, "하지만 실패했다!");
        return false;
    }

    // 참기: 2턴 동안 받은 데미지를 모았다가 3번째 턴에 2배로 돌려준다. 참는 동안은 행동이 정해진다(LOCKED).
    // 돌려줄 차례면 true (이어서 공격으로 처리한다)
    private static bool UseBide(GameRoom room, BattleSide side, PokemonMoveInfo move, int slot)
    {
        BattlePokemon user = side.Current;
        string subject = WithJosa(user.Info.Name, "은", "는");
        if (user.BideTurns == 0)
        {
            LogPokemon(room, side, $"{user.Info.Name}의 {move.Name}!");
            LogPokemon(room, side, $"{subject} 참기 시작했다!");
            user.BideTurns = 2;
            user.BideDamage = 0;
            user.LockedSlot = slot;
            return false;
        }
        if (--user.BideTurns > 0)
        {
            LogPokemon(room, side, $"{subject} 참고 있다!");
            return false;
        }
        user.LockedSlot = null;
        LogPokemon(room, side, $"{subject} 참았던 힘을 풀어놓았다!");
        if (user.BideDamage > 0)
            return true;
        LogPokemon(room, side, $"{user.Info.Name}의 {move.Name}!");
        LogBoth(room, side, "하지만 실패했다!");
        return false;
    }

    // 정신차리기·잠깨움뺨치기: 맞은 상대의 마비·잠듦을 고친다. 비밀의힘: 30% 확률로 필드에 따라 마비(필드 없음·일렉트릭필드)
    // ·잠듦(그래스필드)·특수공격 -1(미스트필드)
    private void ApplyPowerMoveEffects(GameRoom room, BattleSide attacker, BattleSide defender, PokemonMoveInfo move)
    {
        BattlePokemon target = defender.Current;
        if (move.Id == SmellingSaltsMoveId && target.Status == Paralysis)
        {
            target.Status = null;
            LogPokemon(room, defender, $"{target.Info.Name}의 마비가 풀렸다!");
        }
        if (move.Id == WakeUpSlapMoveId && target.Status == Sleep)
        {
            target.Status = null;
            LogPokemon(room, defender, $"{WithJosa(target.Info.Name, "은", "는")} 눈을 떴다!");
        }
        if (move.Id == SecretPowerMoveId && Random.Shared.Next(100) < 30)
        {
            if (room.Terrain == MistyTerrain)
            {
                if (defender.MistTurns == 0)
                    ChangeStage(room, defender, Stat.SpAttack, -1, announceFailure: false);
            }
            else
                TryInflict(room, defender, room.Terrain == GrassyTerrain ? Sleep : Paralysis, announceFailure: false, fromOpponent: true);
        }
    }

    // 싱크로노이즈: 자신과 같은 타입이 있는 상대에게만 맞는다.
    private static bool SharesType(BattlePokemon a, BattlePokemon b) =>
        a.Types.Intersect(b.Types).Any();
}
