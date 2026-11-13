// 능력치 랭크, 급소, 우리 편·필드 효과를 다루는 기술. 수치는 6세대 규칙을 따른다.
// 기술의 능력치 변화(칼춤 공격 +2 등)는 DB의 MoveStatChange에서 읽는다.
partial class RoomManager
{
    private static readonly string[] StatNames = ["공격", "방어", "특수공격", "특수방어", "스피드", "명중률", "회피율"];

    // DB(MoveStatChange)의 능력치 이름 → Stat
    private static readonly Dictionary<string, Stat> StatByIdentifier = new()
    {
        ["attack"] = Stat.Attack, ["defense"] = Stat.Defense, ["special-attack"] = Stat.SpAttack,
        ["special-defense"] = Stat.SpDefense, ["speed"] = Stat.Speed, ["accuracy"] = Stat.Accuracy,
        ["evasion"] = Stat.Evasion,
    };

    // DB에 능력치 변화가 있어도 조건이 붙거나 다른 효과와 함께 처리해야 해서, 능력치만 바꾸는 일반 처리에서 뺀 변화 기술
    // 일구기·플라워가드(풀 타입만, UseEffectMove), 막말내뱉기(교체도 함, UseSwitchMove),
    // 아로마미스트·자기장조작(이 게임에서는 실패). 베놈트랩(독 상태인 상대만)과 지오컨트롤(2턴 기술)은 DB 값을 쓰고 조건만 따로 본다.
    private static readonly HashSet<int> ConditionalStatMoveIds = [563, 575, 579, 597, 602];

    // 능력치만 바꾸는 변화 기술이면 DB의 능력치 변화를 Stat 순서로 돌려준다. 게임 범위인 6세대까지의 기술만 다룬다.
    // 비축하기·충전·추억의선물·안개제거 등 다른 효과가 함께 있는 기술은 UseSpecialMove가 처리한다.
    private bool TryGetStatChanges(PokemonMoveInfo move, out (Stat Stat, int Change, string Target)[] changes)
    {
        changes = data.MoveStatChangesByMoveId[move.Id]
            .Select(change => (Stat: StatByIdentifier[change.Stat], change.Change, change.Target))
            .OrderBy(change => change.Stat)
            .ToArray();
        return move.Category == "변화" && changes.Length > 0 && move.Generation <= 6 &&
               !SpecialMoveIds.Contains(move.Id) && !ConditionalStatMoveIds.Contains(move.Id);
    }

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

    // 명중 판정. 명중률이 없는 기술은 반드시 맞는다. 사용자의 명중률 랭크 - 상대의 회피율 랭크를 반영하고,
    // 중력 중에는 명중률이 5/3배가 된다.
    // ignoreEvasion: 상대의 회피율 랭크를 무시한다 (야금야금·성스러운칼)
    private static bool AccuracyCheck(GameRoom room, BattlePokemon user, BattlePokemon target, PokemonMoveInfo move,
        bool ignoreEvasion = false)
    {
        // 록온·마음의눈으로 노린 상대와 텔레키네시스로 떠오른 상대에게는 반드시 맞는다.
        if (move.Accuracy is not int accuracy || ReferenceEquals(user.LockOnTarget, target) || target.TelekinesisTurns > 0)
            return true;
        int evasion = ignoreEvasion ? 0 : target.GetStage(Stat.Evasion);
        if ((target.Identified || target.MiracleEye) && evasion > 0)
            evasion = 0;    // 꿰뚫어보기·냄새구별·미라클아이: 오른 회피율을 무시한다.
        int stage = Math.Clamp(user.GetStage(Stat.Accuracy) - evasion, -6, 6);
        double gravity = room.GravityTurns > 0 ? 5.0 / 3 : 1;
        return Random.Shared.Next(100) < accuracy * AccuracyMultiplier(stage) * gravity;
    }

    // 급소 판정 (6세대): 급소 랭크 0 → 1/16, 1 → 1/8, 2 → 1/2, 3 이상 → 반드시.
    // 급소 랭크 = 기충전(+2) + 급소에 잘 맞는 기술(DB의 crit_rate, 업어후리기·얼음숨결은 6이라 반드시 급소).
    // 상대 편에 주술이 걸려 있으면 급소에 맞지 않는다.
    private static bool RollCritical(BattlePokemon attacker, BattleSide defender, int moveCritRate)
    {
        if (defender.LuckyChantTurns > 0)
            return false;
        int stage = (attacker.FocusEnergy ? 2 : 0) + moveCritRate;
        return stage >= 3 || Random.Shared.Next(stage switch { 0 => 16, 1 => 8, _ => 2 }) == 0;
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
        (Stat Stat, int Change, string Target)[] changes)
    {
        // 흰안개: 상대가 쓰는 능력치 하락을 막는다. (뽐내기처럼 상대 능력치를 올리는 기술은 막지 않는다.)
        if (defender.MistTurns > 0 && changes.Any(change => change.Target == "opponent" && change.Change < 0))
        {
            LogPokemon(room, defender, $"{WithJosa(defender.Current.Info.Name, "은", "는")} 흰안개에 보호되고 있다!");
            return;
        }
        foreach (var (stat, change, target) in changes)
            ChangeStage(room, target == "self" ? attacker : defender, stat, change, announceFailure: true);
    }

    // 실제 변화 방향마다 한 번만 연출한다. 배턴터치의 교체·랭크 전달은 증감으로 처리하지 않는다.
    private static bool SendStatEffects(GameRoom room, BattleSide side, BattlePokemon original,
        int[] before, bool focusEnergyBefore, int typeId)
    {
        if (!ReferenceEquals(side.Current, original)) return false;
        bool raised = !focusEnergyBefore && original.FocusEnergy;
        bool lowered = focusEnergyBefore && !original.FocusEnergy;
        for (int index = 0; index < before.Length; index++)
        {
            raised |= original.Stages[index] > before[index];
            lowered |= original.Stages[index] < before[index];
        }
        foreach (string direction in new[] { "UP", "DOWN" })
        {
            if (direction == "UP" ? !raised : !lowered) continue;
            side.Player.QueueMessage($"BATTLE_STAT {typeId} {direction} SELF");
            GetOther(room, side).Player.QueueMessage($"BATTLE_STAT {typeId} {direction} OPPONENT");
        }
        return raised;
    }

    // 자신·우리 편의 보호·지속 효과. 실패하거나 턴이 지나는 것만으로는 늘지 않는 값을 비교한다.
    private static int[] GetBuffLevels(BattleSide side)
    {
        BattlePokemon pokemon = side.Current;
        return [
            side.ReflectTurns, side.LightScreenTurns, side.TailwindTurns, side.MistTurns,
            side.LuckyChantTurns, side.SafeguardTurns, side.WishTurns,
            side.QuickGuard ? 1 : 0, side.WideGuard ? 1 : 0, side.MatBlock ? 1 : 0, side.CraftyShield ? 1 : 0,
            pokemon.Protected ? 1 : 0, pokemon.Enduring ? 1 : 0, pokemon.MagnetRiseTurns,
            pokemon.MagicCoat ? 1 : 0, pokemon.SubstituteHp,
            pokemon.AquaRing ? 1 : 0, pokemon.Ingrained ? 1 : 0, pokemon.StockpileCount, pokemon.ChargeTurns
        ];
    }

    private static bool HasNewBuff(BattleSide side, int[] before)
    {
        int[] after = GetBuffLevels(side);
        for (int index = 0; index < before.Length; index++)
            if (after[index] > before[index]) return true;
        return false;
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
                // 6세대부터 양쪽 편의 설치기도 없앤다.
                if (ClearHazards(attacker) | ClearHazards(defender))
                    LogBoth(room, attacker, "설치기가 모두 사라졌다!");
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

    // 배턴터치: 다음 포켓몬을 골라(턴 도중 교체) 랭크·기충전·혼란·아쿠아링·뿌리박기·대타를 그대로 넘겨준다.
    // 교체할 포켓몬이 없으면 실패한다. 넘겨주는 처리는 CompleteMidTurnSwitch에서 한다.
    private static void UseBatonPass(GameRoom room, BattleSide side)
    {
        if (!StartMidTurnSwitch(room, side, batonPass: true))
            LogBoth(room, side, "하지만 실패했다!");
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
