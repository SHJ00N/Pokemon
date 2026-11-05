// 능력치 랭크, 급소, 우리 편·필드 효과를 다루는 기술. 수치는 5세대 규칙을 따른다.
// 능력치 변화 데이터는 아직 DB에 없어서 여기에 직접 적었다. 나중에 DB 테이블로 옮긴다. (README "남은 작업")
partial class RoomManager
{
    private static readonly string[] StatNames = ["공격", "방어", "특수공격", "특수방어", "스피드", "명중률", "회피율"];

    // 기술 번호 → 자신에게 쓰는지(Self), 바뀌는 능력치와 단계
    private static readonly Dictionary<int, (bool Self, (Stat Stat, int Stages)[] Changes)> StatChangeMoves = new()
    {
        // 자신의 능력치를 올리는 기술
        [14] = (true, [(Stat.Attack, 2)]),                                           // 칼춤
        [417] = (true, [(Stat.SpAttack, 2)]),                                        // 나쁜음모
        [334] = (true, [(Stat.Defense, 2)]),                                         // 철벽
        [133] = (true, [(Stat.SpDefense, 2)]),                                       // 망각술
        [97] = (true, [(Stat.Speed, 2)]),                                            // 고속이동
        [104] = (true, [(Stat.Evasion, 1)]),                                         // 그림자분신
        [347] = (true, [(Stat.SpAttack, 1), (Stat.SpDefense, 1)]),                   // 명상
        [339] = (true, [(Stat.Attack, 1), (Stat.Defense, 1)]),                       // 벌크업
        [349] = (true, [(Stat.Attack, 1), (Stat.Speed, 1)]),                         // 용의춤
        [74] = (true, [(Stat.Attack, 1), (Stat.SpAttack, 1)]),                       // 성장 (날씨가 없으므로 항상 +1)
        [504] = (true, [(Stat.Attack, 2), (Stat.SpAttack, 2), (Stat.Speed, 2),
                        (Stat.Defense, -1), (Stat.SpDefense, -1)]),                  // 껍질깨기
        [322] = (true, [(Stat.Defense, 1), (Stat.SpDefense, 1)]),                    // 코스믹파워
        [489] = (true, [(Stat.Attack, 1), (Stat.Defense, 1), (Stat.Accuracy, 1)]),   // 똬리틀기
        [96] = (true, [(Stat.Attack, 1)]),                                           // 요가포즈
        [106] = (true, [(Stat.Defense, 1)]),                                         // 단단해지기
        [107] = (true, [(Stat.Evasion, 2)]),                                         // 작아지기 (5세대 +2)
        [110] = (true, [(Stat.Defense, 1)]),                                         // 껍질에숨기
        [111] = (true, [(Stat.Defense, 1)]),                                         // 웅크리기
        [112] = (true, [(Stat.Defense, 2)]),                                         // 배리어
        [151] = (true, [(Stat.Defense, 2)]),                                         // 녹기
        [159] = (true, [(Stat.Attack, 1)]),                                          // 각지기
        [294] = (true, [(Stat.SpAttack, 3)]),                                        // 반딧불 (5세대 +3)
        [336] = (true, [(Stat.Attack, 1)]),                                          // 멀리짖기
        [397] = (true, [(Stat.Speed, 2)]),                                           // 록커트
        [455] = (true, [(Stat.Defense, 1), (Stat.SpDefense, 1)]),                    // 방어지령
        [468] = (true, [(Stat.Attack, 1), (Stat.Accuracy, 1)]),                      // 손톱갈기
        [475] = (true, [(Stat.Speed, 2)]),                                           // 바디퍼지 (무게 감소는 쓰이지 않음)
        [483] = (true, [(Stat.SpAttack, 1), (Stat.SpDefense, 1), (Stat.Speed, 1)]),  // 나비춤
        [508] = (true, [(Stat.Attack, 1), (Stat.Speed, 2)]),                         // 기어체인지
        [526] = (true, [(Stat.Attack, 1), (Stat.SpAttack, 1)]),                      // 분발
        [538] = (true, [(Stat.Defense, 3)]),                                         // 코튼가드

        // 상대의 능력치를 내리는 기술
        [45] = (false, [(Stat.Attack, -1)]),                                         // 울음소리
        [39] = (false, [(Stat.Defense, -1)]),                                        // 꼬리흔들기
        [43] = (false, [(Stat.Defense, -1)]),                                        // 째려보기
        [103] = (false, [(Stat.Defense, -2)]),                                       // 싫은소리
        [319] = (false, [(Stat.SpDefense, -2)]),                                     // 금속음
        [313] = (false, [(Stat.SpDefense, -2)]),                                     // 거짓울음
        [204] = (false, [(Stat.Attack, -2)]),                                        // 애교부리기
        [28] = (false, [(Stat.Accuracy, -1)]),                                       // 모래뿌리기
        [108] = (false, [(Stat.Accuracy, -1)]),                                      // 연막
        [230] = (false, [(Stat.Evasion, -1)]),                                       // 달콤한향기 (5세대 -1)
        [321] = (false, [(Stat.Attack, -1), (Stat.Defense, -1)]),                    // 간지르기
        [81] = (false, [(Stat.Speed, -1)]),                                          // 실뿜기 (5세대 -1)
        [134] = (false, [(Stat.Accuracy, -1)]),                                      // 숟가락휘기
        [148] = (false, [(Stat.Accuracy, -1)]),                                      // 플래시
        [178] = (false, [(Stat.Speed, -2)]),                                         // 목화포자
        [184] = (false, [(Stat.Speed, -2)]),                                         // 겁나는얼굴
        [297] = (false, [(Stat.Attack, -2)]),                                        // 깃털댄스
        [445] = (false, [(Stat.SpAttack, -2)]),                                      // 유혹 (성별이 없어서 항상 적용)

        // 상대의 능력치를 올리고 혼란에 빠뜨리는 기술 (혼란은 DB의 MoveStatusEffect로 처리)
        [207] = (false, [(Stat.Attack, 2)]),                                         // 뽐내기
        [260] = (false, [(Stat.SpAttack, 1)]),                                       // 부추기기
    };

    private const int ReflectMoveId = 115, LightScreenMoveId = 113, TailwindMoveId = 366, TrickRoomMoveId = 433,
        MistMoveId = 54, FocusEnergyMoveId = 116, LuckyChantMoveId = 381, HazeMoveId = 114, ClearSmogMoveId = 499,
        DefogMoveId = 432, PowerSplitMoveId = 471, GuardSplitMoveId = 470, BatonPassMoveId = 226,
        PsychUpMoveId = 244, PowerSwapMoveId = 384, GuardSwapMoveId = 385, HeartSwapMoveId = 391,
        BellyDrumMoveId = 187, MementoMoveId = 262, StockpileMoveId = 254, ChargeMoveId = 268, AcupressureMoveId = 367;

    // UseSpecialMove에서 처리하는 변화 기술
    private static readonly HashSet<int> SpecialMoveIds =
    [
        ReflectMoveId, LightScreenMoveId, TailwindMoveId, TrickRoomMoveId, MistMoveId, FocusEnergyMoveId,
        LuckyChantMoveId, HazeMoveId, DefogMoveId, PowerSplitMoveId, GuardSplitMoveId, BatonPassMoveId,
        PsychUpMoveId, PowerSwapMoveId, GuardSwapMoveId, HeartSwapMoveId,
        BellyDrumMoveId, MementoMoveId, StockpileMoveId, ChargeMoveId, AcupressureMoveId
    ];

    // 공격·방어·특공·특방·스피드 랭크 배율: +1 ×1.5, +2 ×2 ... / -1 ×2/3, -2 ×1/2 ...
    private static double StageMultiplier(int stage) => stage >= 0 ? (2 + stage) / 2.0 : 2.0 / (2 - stage);

    // 명중률·회피율 랭크 배율: +1 ×4/3, +2 ×5/3 ... / -1 ×3/4, -2 ×3/5 ...
    private static double AccuracyMultiplier(int stage) => stage >= 0 ? (3 + stage) / 3.0 : 3.0 / (3 - stage);

    // 행동 순서에 쓰는 스피드: 랭크, 마비(1/4), 순풍(2배)을 반영한다.
    private static int GetSpeed(BattleSide side)
    {
        BattlePokemon pokemon = side.Current;
        double speed = pokemon.Speed * StageMultiplier(pokemon.GetStage(Stat.Speed));
        if (pokemon.Status == Paralysis) speed /= 4;
        if (side.TailwindTurns > 0) speed *= 2;
        return (int)speed;
    }

    // 명중 판정. 명중률이 없는 기술은 반드시 맞는다. 사용자의 명중률 랭크 - 상대의 회피율 랭크를 반영한다.
    private static bool AccuracyCheck(BattlePokemon user, BattlePokemon target, PokemonMoveInfo move)
    {
        if (move.Accuracy is not int accuracy)
            return true;
        int stage = Math.Clamp(user.GetStage(Stat.Accuracy) - target.GetStage(Stat.Evasion), -6, 6);
        return Random.Shared.Next(100) < accuracy * AccuracyMultiplier(stage);
    }

    // 급소 판정: 기본 1/16, 기충전이면 1/4. 상대 편에 주술이 걸려 있으면 급소에 맞지 않는다.
    private static bool RollCritical(BattlePokemon attacker, BattleSide defender)
    {
        if (defender.LuckyChantTurns > 0)
            return false;
        return Random.Shared.Next(attacker.FocusEnergy ? 4 : 16) == 0;
    }

    // 랭크를 바꾸고 기록을 남긴다. 이미 한계(+6/-6)면 바뀌지 않는다. 실제로 바뀐 단계를 돌려준다.
    private static int ChangeStage(GameRoom room, BattleSide side, Stat stat, int delta, bool announceFailure)
    {
        BattlePokemon pokemon = side.Current;
        int before = pokemon.GetStage(stat);
        int after = Math.Clamp(before + delta, -6, 6);
        string statName = $"{pokemon.Info.Name}의 {WithJosa(StatNames[(int)stat], "은", "는")}";
        if (after == before)
        {
            if (announceFailure && delta != 0)
                LogPokemon(room, side, $"{statName} 더 이상 {(delta > 0 ? "올라가지" : "떨어지지")} 않는다!");
            return 0;
        }

        pokemon.Stages[(int)stat] = after;
        string how = (after - before) switch
        {
            >= 3 => "매우 크게 올랐다!",
            2 => "크게 올랐다!",
            1 => "올랐다!",
            -1 => "떨어졌다!",
            -2 => "크게 떨어졌다!",
            _ => "매우 크게 떨어졌다!"
        };
        LogPokemon(room, side, $"{pokemon.Info.Name}의 {WithJosa(StatNames[(int)stat], "이", "가")} {how}");
        return after - before;
    }

    private static void UseStatChangeMove(GameRoom room, BattleSide attacker, BattleSide defender,
        (bool Self, (Stat Stat, int Stages)[] Changes) entry)
    {
        BattleSide target = entry.Self ? attacker : defender;
        // 흰안개: 상대가 쓰는 능력치 하락을 막는다. (뽐내기처럼 상대 능력치를 올리는 기술은 막지 않는다.)
        if (!entry.Self && target.MistTurns > 0 && entry.Changes.Any(change => change.Stages < 0))
        {
            LogPokemon(room, target, $"{WithJosa(target.Current.Info.Name, "은", "는")} 흰안개에 보호되고 있다!");
            return;
        }
        foreach (var (stat, stages) in entry.Changes)
            ChangeStage(room, target, stat, stages, announceFailure: true);
    }

    // 랭크 대신 우리 편·필드 효과를 걸거나, 능력치를 나누고 바꾸는 기술
    private static void UseSpecialMove(GameRoom room, BattleSide attacker, BattleSide defender, PokemonMoveInfo move)
    {
        BattlePokemon user = attacker.Current, target = defender.Current;
        string subject = WithJosa(user.Info.Name, "은", "는");
        switch (move.Id)
        {
            case ReflectMoveId:
                StartSideEffect(room, attacker, attacker.ReflectTurns, 5, value => attacker.ReflectTurns = value,
                    "리플렉터로 {0}이 물리 공격에 강해졌다!");
                break;
            case LightScreenMoveId:
                StartSideEffect(room, attacker, attacker.LightScreenTurns, 5, value => attacker.LightScreenTurns = value,
                    "빛의장막으로 {0}이 특수 공격에 강해졌다!");
                break;
            case TailwindMoveId:
                StartSideEffect(room, attacker, attacker.TailwindTurns, 4, value => attacker.TailwindTurns = value,
                    "{0} 뒤에서 순풍이 불기 시작했다!");
                break;
            case MistMoveId:
                StartSideEffect(room, attacker, attacker.MistTurns, 5, value => attacker.MistTurns = value,
                    "{0}이 흰안개에 둘러싸였다!");
                break;
            case LuckyChantMoveId:
                StartSideEffect(room, attacker, attacker.LuckyChantTurns, 5, value => attacker.LuckyChantTurns = value,
                    "주술로 {0}이 급소에 맞지 않게 되었다!");
                break;
            case FocusEnergyMoveId:
                if (user.FocusEnergy)
                    LogBoth(room, attacker, "하지만 실패했다!");
                else
                {
                    user.FocusEnergy = true;
                    LogPokemon(room, attacker, $"{subject} 의욕이 넘치고 있다!");
                }
                break;
            case TrickRoomMoveId:
                // 트릭룸 중에 다시 쓰면 원래대로 돌아온다.
                room.TrickRoomTurns = room.TrickRoomTurns > 0 ? 0 : 5;
                LogBoth(room, attacker, room.TrickRoomTurns > 0 ? "시공이 뒤틀렸다!" : "뒤틀린 시공이 원래대로 돌아왔다!");
                break;
            case HazeMoveId:
                Array.Clear(user.Stages);
                Array.Clear(target.Stages);
                LogBoth(room, attacker, "모든 능력 변화가 원래대로 돌아왔다!");
                break;
            case DefogMoveId:
                // 상대 회피율 -1, 상대 편의 리플렉터·빛의장막·흰안개를 없앤다.
                if (defender.MistTurns > 0)
                    LogPokemon(room, defender, $"{WithJosa(target.Info.Name, "은", "는")} 흰안개에 보호되고 있다!");
                else
                    ChangeStage(room, defender, Stat.Evasion, -1, announceFailure: true);
                if (defender.ReflectTurns + defender.LightScreenTurns + defender.MistTurns + defender.SafeguardTurns > 0)
                {
                    defender.ReflectTurns = defender.LightScreenTurns = defender.MistTurns = defender.SafeguardTurns = 0;
                    LogSide(room, defender, "{0}의 리플렉터·빛의장막·흰안개·신비의부적이 사라졌다!");
                }
                break;
            case PowerSplitMoveId:
                (user.Attack, target.Attack) = Average(user.Attack, target.Attack);
                (user.SpAttack, target.SpAttack) = Average(user.SpAttack, target.SpAttack);
                LogPokemon(room, attacker, $"{subject} 상대와 공격과 특수공격을 나눠 가졌다!");
                break;
            case GuardSplitMoveId:
                (user.Defense, target.Defense) = Average(user.Defense, target.Defense);
                (user.SpDefense, target.SpDefense) = Average(user.SpDefense, target.SpDefense);
                LogPokemon(room, attacker, $"{subject} 상대와 방어와 특수방어를 나눠 가졌다!");
                break;
            case PsychUpMoveId:
                target.Stages.CopyTo(user.Stages, 0);
                LogPokemon(room, attacker, $"{subject} 상대의 능력 변화를 복사했다!");
                break;
            case PowerSwapMoveId:
                SwapStages(user, target, Stat.Attack, Stat.SpAttack);
                LogPokemon(room, attacker, $"{subject} 상대와 공격·특수공격의 능력 변화를 바꿨다!");
                break;
            case GuardSwapMoveId:
                SwapStages(user, target, Stat.Defense, Stat.SpDefense);
                LogPokemon(room, attacker, $"{subject} 상대와 방어·특수방어의 능력 변화를 바꿨다!");
                break;
            case HeartSwapMoveId:
                SwapStages(user, target, Enum.GetValues<Stat>());
                LogPokemon(room, attacker, $"{subject} 상대와 능력 변화를 바꿨다!");
                break;
            case BatonPassMoveId:
                UseBatonPass(room, attacker);
                break;
            case BellyDrumMoveId:
                // 최대 HP의 절반을 쓰고 공격을 +6으로 만든다. HP가 절반 이하거나 이미 +6이면 실패한다.
                if (user.Hp <= user.MaxHp / 2 || user.GetStage(Stat.Attack) == 6)
                    LogBoth(room, attacker, "하지만 실패했다!");
                else
                {
                    user.Hp -= user.MaxHp / 2;
                    user.Stages[(int)Stat.Attack] = 6;
                    LogPokemon(room, attacker, $"{subject} 체력을 깎아서 공격을 최대로 올렸다!");
                }
                break;
            case MementoMoveId:
                // 상대의 공격·특수공격을 2단계씩 내리고 자신은 쓰러진다. (흰안개가 있어도 쓰러진다.)
                if (defender.MistTurns > 0)
                    LogPokemon(room, defender, $"{WithJosa(target.Info.Name, "은", "는")} 흰안개에 보호되고 있다!");
                else
                {
                    ChangeStage(room, defender, Stat.Attack, -2, announceFailure: true);
                    ChangeStage(room, defender, Stat.SpAttack, -2, announceFailure: true);
                }
                DealDamage(room, attacker, user.Hp);
                break;
            case StockpileMoveId:
                // 최대 3번까지 쌓고, 쌓을 때마다 방어·특수방어 +1
                if (user.StockpileCount == 3)
                    LogBoth(room, attacker, "하지만 실패했다!");
                else
                {
                    user.StockpileCount++;
                    LogPokemon(room, attacker, $"{subject} {user.StockpileCount}개 비축했다!");
                    user.StockpileDefense += ChangeStage(room, attacker, Stat.Defense, 1, announceFailure: true);
                    user.StockpileSpDefense += ChangeStage(room, attacker, Stat.SpDefense, 1, announceFailure: true);
                }
                break;
            case ChargeMoveId:
                // 특수방어 +1, 다음 턴까지 전기 기술 위력 2배
                user.ChargeTurns = 2;
                LogPokemon(room, attacker, $"{subject} 전기를 모으기 시작했다!");
                ChangeStage(room, attacker, Stat.SpDefense, 1, announceFailure: true);
                break;
            case AcupressureMoveId:
                // 아직 +6이 아닌 능력치 중 하나를 무작위로 2단계 올린다.
                Stat[] candidates = Enum.GetValues<Stat>().Where(stat => user.GetStage(stat) < 6).ToArray();
                if (candidates.Length == 0)
                    LogBoth(room, attacker, "하지만 실패했다!");
                else
                    ChangeStage(room, attacker, candidates[Random.Shared.Next(candidates.Length)], 2, announceFailure: true);
                break;
        }
    }

    // 배턴터치: 고른 포켓몬과 교체하고, 랭크·기충전·혼란·아쿠아링·뿌리박기·대타를 그대로 넘겨준다.
    private static void UseBatonPass(GameRoom room, BattleSide side)
    {
        if (side.BatonTarget is not int index || side.Team[index].Hp == 0)
        {
            LogBoth(room, side, "하지만 실패했다!");
            return;
        }

        BattlePokemon previous = side.Current;
        int[] stages = previous.Stages.ToArray();
        bool focusEnergy = previous.FocusEnergy, aquaRing = previous.AquaRing, ingrained = previous.Ingrained;
        int confusion = previous.ConfusionTurns, substitute = previous.SubstituteHp;

        ChangeActive(room, side, index);
        BattlePokemon next = side.Current;
        stages.CopyTo(next.Stages, 0);
        next.FocusEnergy = focusEnergy;
        next.ConfusionTurns = confusion;
        next.AquaRing = aquaRing;
        next.Ingrained = ingrained;
        next.SubstituteHp = substitute;
        Log(room, side, $"돌아와, {previous.Info.Name}! 가라, {next.Info.Name}!",
            $"상대가 {WithJosa(previous.Info.Name, "을", "를")} 거두고 {WithJosa(next.Info.Name, "을", "를")} 내보냈다!");
    }

    private static void StartSideEffect(GameRoom room, BattleSide side, int turnsLeft, int duration,
        Action<int> setTurns, string message)
    {
        if (turnsLeft > 0)
        {
            LogBoth(room, side, "하지만 실패했다!");
            return;
        }
        setTurns(duration);
        LogSide(room, side, message);
    }

    // 턴 끝: 우리 편·필드 효과의 남은 턴을 줄이고, 끝나면 알린다.
    private static void TickSideEffects(GameRoom room)
    {
        foreach (BattleSide side in new[] { room.HostSide!, room.GuestSide! })
        {
            side.ReflectTurns = Tick(room, side, side.ReflectTurns, "리플렉터");
            side.LightScreenTurns = Tick(room, side, side.LightScreenTurns, "빛의장막");
            side.TailwindTurns = Tick(room, side, side.TailwindTurns, "순풍");
            side.MistTurns = Tick(room, side, side.MistTurns, "흰안개");
            side.LuckyChantTurns = Tick(room, side, side.LuckyChantTurns, "주술");
            side.SafeguardTurns = Tick(room, side, side.SafeguardTurns, "신비의부적");
        }
        if (room.TrickRoomTurns > 0 && --room.TrickRoomTurns == 0)
            LogBoth(room, room.HostSide!, "뒤틀린 시공이 원래대로 돌아왔다!");

        foreach (BattleSide side in new[] { room.HostSide!, room.GuestSide! })
            if (side.Current.ChargeTurns > 0)
                side.Current.ChargeTurns--;
    }

    private static int Tick(GameRoom room, BattleSide side, int turnsLeft, string effect)
    {
        if (turnsLeft == 0)
            return 0;
        if (--turnsLeft == 0)
            LogSide(room, side, "{0}의 " + effect + " 효과가 사라졌다!");
        return turnsLeft;
    }

    private static (int, int) Average(int a, int b) => ((a + b) / 2, (a + b) / 2);

    private static void SwapStages(BattlePokemon a, BattlePokemon b, params Stat[] stats)
    {
        foreach (Stat stat in stats)
            (a.Stages[(int)stat], b.Stages[(int)stat]) = (b.Stages[(int)stat], a.Stages[(int)stat]);
    }

    // 화면 표시용: 0이 아닌 랭크와 기충전. 예: ["공격 +2", "스피드 -1"]
    private static string[] DescribeStages(BattlePokemon pokemon)
    {
        var changes = Enum.GetValues<Stat>()
            .Where(stat => pokemon.GetStage(stat) != 0)
            .Select(stat => $"{StatNames[(int)stat]} {pokemon.GetStage(stat):+0;-0}")
            .ToList();
        if (pokemon.FocusEnergy)
            changes.Add("기충전");
        if (pokemon.AquaRing)
            changes.Add("아쿠아링");
        if (pokemon.Ingrained)
            changes.Add("뿌리박기");
        if (pokemon.SubstituteHp > 0)
            changes.Add("대타");
        return changes.ToArray();
    }

    // 우리 편 전체 이야기: side 플레이어에게는 "우리 편", 상대에게는 "상대 편"으로 보낸다.
    private static void LogSide(GameRoom room, BattleSide side, string format) =>
        Log(room, side, string.Format(format, "우리 편"), string.Format(format, "상대 편"));
}
