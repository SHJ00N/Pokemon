// 기술 실행과 상태이상 처리. 수치는 5세대 규칙을 따른다.
// 상태이상 종류와 기술별 확률·대상·시점은 DB의 StatusCondition, MoveStatusEffect에서 읽는다.
partial class RoomManager
{
    // StatusCondition.code
    private const string Paralysis = "paralysis", Sleep = "sleep", Freeze = "freeze", Burn = "burn",
        Poison = "poison", BadPoison = "bad-poison", Confusion = "confusion", Infatuation = "infatuation";

    private const int RestMoveId = 156;         // 잠자기: HP를 모두 회복하고 3턴 잠든다.
    private const int ThunderWaveMoveId = 86;   // 전기자석파: 변화 기술 중 유일하게 땅 타입에게 효과가 없다.
    // 얼어 있어도 쓰면 스스로 얼음이 녹는 기술: 화염바퀴, 성스러운불꽃, 플레어드라이브, 열탕, 크로스플레임
    private static readonly HashSet<int> SelfThawMoveIds = [172, 221, 394, 503, 558];

    // 한 포켓몬의 기술 한 번을 처리한다. 행동 가능 판정 → 방어·매직코트 → 기술 효과 → 난동 기술 종료 순서다.
    // movesLast: 이번 턴에 이 포켓몬 뒤로 행동할 포켓몬이 없는지 (방어·매직코트는 이때 실패한다)
    private void ExecuteMove(GameRoom room, BattleSide attacker, BattleSide defender, bool movesLast)
    {
        BattlePokemon user = attacker.Current;
        // 이번 턴에 먼저 쓰러졌거나, 상대가 이미 쓰러졌으면 기술을 쓰지 않는다.
        if (user.Hp == 0 || defender.Current.Hp == 0)
            return;

        int slot = attacker.ChosenMove!.Value;
        PokemonMoveInfo move = user.Moves[slot - 1];
        if (!CanAct(room, attacker, move))
        {
            // 난동 중에 행동하지 못하면 난동이 혼란 없이 끝난다.
            user.LockedSlot = null;
            user.LockTurnsLeft = 0;
            return;
        }

        // 난동 기술(DB의 after_sequence 효과가 있는 기술)은 2~3턴 동안 같은 기술만 쓴다.
        bool rampage = data.MoveStatusEffectsByMoveId[move.Id].Any(e => e.Timing == "after_sequence");
        if (rampage && user.LockedSlot == null)
        {
            user.LockedSlot = slot;
            user.LockTurnsLeft = Random.Shared.Next(2, 4);
        }

        BattlePokemon target = defender.Current;
        if (TargetsOpponent(move) && IsProtectedFrom(defender, move))
        {
            LogPokemon(room, attacker, $"{user.Info.Name}의 {move.Name}!");
            LogPokemon(room, defender, $"{WithJosa(target.Info.Name, "은", "는")} 공격으로부터 몸을 지켰다!");
        }
        else if (target.MagicCoat && IsOpponentStatusMove(move))
        {
            // 매직코트: 상대를 노린 변화 기술을 쓴 쪽에게 되돌려 보낸다.
            LogPokemon(room, attacker, $"{user.Info.Name}의 {move.Name}!");
            LogPokemon(room, defender, $"{WithJosa(target.Info.Name, "은", "는")} {WithJosa(move.Name, "을", "를")} 되받아쳤다!");
            UseStatusMove(room, defender, attacker, move, movesLast: false, reflected: true);
        }
        else if (move.Category == "변화")
            UseStatusMove(room, attacker, defender, move, movesLast);
        else
            UseDamagingMove(room, attacker, defender, move);

        if (rampage && user.LockedSlot != null && --user.LockTurnsLeft == 0)
        {
            user.LockedSlot = null;
            if (user.Hp > 0)
                ApplyStatusEffects(room, attacker, defender, move, "after_sequence", announceFailure: false);
        }
    }

    private void UseDamagingMove(GameRoom room, BattleSide attacker, BattleSide defender, PokemonMoveInfo move)
    {
        BattlePokemon user = attacker.Current, target = defender.Current;
        SendMoveEffect(room, attacker, move);
        var result = CalculateDamage(attacker, defender, move);
        LogPokemon(room, attacker, DescribeMove(user, move, result));
        if (!result.Supported || !result.Hit || result.Effectiveness == 0)
            return;

        // 대타가 대신 맞으면 추가 효과는 없다.
        if (HitSubstitute(room, defender, result.Damage))
            return;

        // 버티기: 이번 턴에는 쓰러질 데미지를 받아도 HP 1로 버틴다.
        int damage = result.Damage;
        bool endured = target.Enduring && damage >= target.Hp;
        if (endured)
            damage = target.Hp - 1;
        DealDamage(room, defender, damage);
        if (endured)
            LogPokemon(room, defender, $"{WithJosa(target.Info.Name, "은", "는")} 공격을 버텼다!");
        if (target.Hp == 0)
            return;

        // 클리어스모그: 맞은 상대의 랭크를 모두 원래대로 되돌린다.
        if (move.Id == ClearSmogMoveId && target.Stages.Any(stage => stage != 0))
        {
            Array.Clear(target.Stages);
            LogPokemon(room, defender, $"{target.Info.Name}의 능력 변화가 원래대로 돌아왔다!");
        }

        // 얼어 있는 포켓몬은 불꽃 타입 공격을 맞으면 녹는다.
        if (target.Status == Freeze && HasTypeName(move.TypeId, "불꽃"))
        {
            target.Status = null;
            LogPokemon(room, defender, $"{target.Info.Name}의 얼음이 녹았다!");
        }
        ApplyStatusEffects(room, attacker, defender, move, null, announceFailure: false);
    }

    private void SendMoveEffect(GameRoom room, BattleSide attacker, PokemonMoveInfo move)
    {
        string color = data.TypesById[move.TypeId].ColorRgb;
        string type = data.TypesById[move.TypeId].Name;
        room.Host.QueueMessage($"BATTLE_EFFECT {move.TypeId} {color} {type} {(attacker == room.HostSide ? "SELF" : "OPPONENT")}");
        room.Guest!.QueueMessage($"BATTLE_EFFECT {move.TypeId} {color} {type} {(attacker == room.GuestSide ? "SELF" : "OPPONENT")}");
    }

    // reflected: 매직코트로 되돌아온 기술 (기술 이름 기록은 이미 남겼다)
    private void UseStatusMove(GameRoom room, BattleSide attacker, BattleSide defender, PokemonMoveInfo move,
        bool movesLast, bool reflected = false)
    {
        BattlePokemon user = attacker.Current;
        string head = $"{user.Info.Name}의 {move.Name}!";
        bool causesStatus = data.MoveStatusEffectsByMoveId[move.Id].Count > 0;
        bool changesStats = StatChangeMoves.TryGetValue(move.Id, out var statChange);
        // 상태이상·능력치 변화·회복·방어·특수 효과가 없는 변화 기술은 아직 구현되지 않았다.
        if (!causesStatus && !changesStats && !SpecialMoveIds.Contains(move.Id) && !HealMoveIds.Contains(move.Id) &&
            !ProtectMoveIds.Contains(move.Id))
        {
            LogPokemon(room, attacker, head + " (아직 구현되지 않은 기술이라 효과가 없었다)");
            return;
        }

        if (!reflected)
            LogPokemon(room, attacker, head);
        if (move.Id == RestMoveId)
        {
            UseRest(room, attacker);
            return;
        }
        // 명중률이 있는 기술(상대를 노리는 기술)만 명중 판정을 한다.
        if (!AccuracyCheck(user, defender.Current, move))
        {
            LogBoth(room, attacker, "하지만 빗나갔다!");
            return;
        }
        if (move.Id == ThunderWaveMoveId && TypeMultiplier(move, defender.Current) == 0)
        {
            LogBoth(room, attacker, "효과가 없는 것 같다...");
            return;
        }
        // 대타가 있으면 상대를 노린 변화 기술은 실패한다.
        if (IsOpponentStatusMove(move) && defender.Current.SubstituteHp > 0)
        {
            LogBoth(room, attacker, "하지만 실패했다!");
            return;
        }

        if (changesStats)
            UseStatChangeMove(room, attacker, defender, statChange);
        else if (SpecialMoveIds.Contains(move.Id))
            UseSpecialMove(room, attacker, defender, move);
        else if (HealMoveIds.Contains(move.Id))
            UseHealMove(room, attacker, defender, move);
        else if (ProtectMoveIds.Contains(move.Id))
            UseProtectMove(room, attacker, move, movesLast);
        if (causesStatus)
            ApplyStatusEffects(room, attacker, defender, move, null, announceFailure: true);
    }

    private static void UseRest(GameRoom room, BattleSide side)
    {
        BattlePokemon user = side.Current;
        if (user.Hp == user.MaxHp || user.Status == Sleep)
        {
            LogBoth(room, side, "하지만 실패했다!");
            return;
        }
        // 다른 상태이상이 있어도 잠듦으로 바뀐다. 두 번 행동하지 못하고 세 번째에 깨어나 행동한다.
        user.Status = Sleep;
        user.SleepStart = user.SleepTurns = 3;
        user.Hp = user.MaxHp;
        LogPokemon(room, side, $"{WithJosa(user.Info.Name, "은", "는")} 잠들어서 건강해졌다!");
    }

    // 기술의 상태이상 효과를 적용한다. timing이 null이면 난동 종료(after_sequence)를 뺀 사용 시점 효과 전부다.
    // effect_group 0은 효과마다 따로 판정하고, 같은 양수 그룹은 그중 최대 하나만 추첨한다(예: 트라이어택).
    private void ApplyStatusEffects(GameRoom room, BattleSide attacker, BattleSide defender,
        PokemonMoveInfo move, string? timing, bool announceFailure)
    {
        var effects = data.MoveStatusEffectsByMoveId[move.Id]
            .Where(e => timing == null ? e.Timing != "after_sequence" : e.Timing == timing);
        foreach (var group in effects.GroupBy(e => e.EffectGroup == 0 ? (object)e : (e.EffectGroup, e.Target, e.Timing)))
        {
            double roll = Random.Shared.NextDouble() * 100;
            MoveStatusEffectInfo? chosen = null;
            double total = 0;
            foreach (MoveStatusEffectInfo effect in group)
            {
                total += effect.Chance;
                if (roll < total)
                {
                    chosen = effect;
                    break;
                }
            }
            if (chosen == null)
                continue;

            BattleSide target = chosen.Target == "self" ? attacker : defender;
            string code = data.StatusConditionsById[chosen.StatusId].Code;
            if (chosen.Timing == "end_next_turn")
                TryMakeDrowsy(room, target, announceFailure);
            else
                TryInflict(room, target, code, announceFailure, fromOpponent: target != attacker);
        }
    }

    // fromOpponent: 상대 기술로 거는 상태이상인지 (신비의부적은 이것만 막는다)
    private void TryInflict(GameRoom room, BattleSide side, string code, bool announceFailure, bool fromOpponent)
    {
        BattlePokemon target = side.Current;
        if (target.Hp == 0)
            return;

        // 신비의부적: 상대 기술로 걸리는 상태이상과 혼란을 막는다. (헤롱헤롱은 막지 못한다)
        if (fromOpponent && side.SafeguardTurns > 0 && code != Infatuation)
        {
            if (announceFailure)
                LogSide(room, side, "{0}은 신비의 베일에 보호받고 있다!");
            return;
        }

        bool success;
        string text;
        switch (code)
        {
            case Confusion:
                success = target.ConfusionTurns == 0;
                if (success) target.ConfusionTurns = Random.Shared.Next(2, 6);  // 1~4턴 동안 혼란
                text = "혼란에 빠졌다!";
                break;
            case Infatuation:
                success = !target.Infatuated;
                target.Infatuated = true;
                text = "헤롱헤롱해졌다!";
                break;
            default:
                // 교체해도 남는 상태이상은 한 번에 하나만 걸린다.
                success = target.Status == null && !IsImmune(target, code);
                if (success)
                {
                    target.Status = code;
                    if (code == Sleep) target.SleepStart = target.SleepTurns = Random.Shared.Next(2, 5);
                    if (code == BadPoison) target.ToxicCounter = 0;
                }
                text = code switch
                {
                    Paralysis => "마비되어 기술이 나오기 어려워졌다!",
                    Sleep => "잠들어 버렸다!",
                    Freeze => "얼어붙었다!",
                    Burn => "화상을 입었다!",
                    Poison => "독에 걸렸다!",
                    _ => "맹독에 걸렸다!"
                };
                break;
        }

        if (success)
            LogPokemon(room, side, $"{WithJosa(target.Info.Name, "은", "는")} {text}");
        else if (announceFailure)
            LogBoth(room, side, "하지만 실패했다!");
    }

    // 하품: 다음 턴이 끝날 때 잠든다. 신비의부적이 있으면 실패한다.
    private static void TryMakeDrowsy(GameRoom room, BattleSide side, bool announceFailure)
    {
        BattlePokemon target = side.Current;
        if (target.Status != null || target.DrowsyTurns > 0 || side.SafeguardTurns > 0)
        {
            if (announceFailure) LogBoth(room, side, "하지만 실패했다!");
            return;
        }
        target.DrowsyTurns = 2;
        LogPokemon(room, side, $"{WithJosa(target.Info.Name, "은", "는")} 졸음이 오기 시작했다!");
    }

    // 5세대 타입 면역: 불꽃은 화상, 얼음은 얼음, 독·강철은 독과 맹독에 걸리지 않는다.
    private bool IsImmune(BattlePokemon pokemon, string code) => code switch
    {
        Burn => HasType(pokemon, "불꽃"),
        Freeze => HasType(pokemon, "얼음"),
        Poison or BadPoison => HasType(pokemon, "독") || HasType(pokemon, "강철"),
        _ => false
    };

    // 기술을 쓰기 전에 잠듦 → 얼음 → 혼란 → 헤롱헤롱 → 마비 순서로 행동할 수 있는지 판정한다.
    private bool CanAct(GameRoom room, BattleSide side, PokemonMoveInfo move)
    {
        BattlePokemon user = side.Current;
        string subject = WithJosa(user.Info.Name, "은", "는");

        if (user.Status == Sleep)
        {
            if (--user.SleepTurns > 0)
            {
                LogPokemon(room, side, $"{subject} 쿨쿨 잠들어 있다.");
                return false;
            }
            user.Status = null;
            LogPokemon(room, side, $"{subject} 눈을 떴다!");
        }

        if (user.Status == Freeze)
        {
            if (!SelfThawMoveIds.Contains(move.Id) && Random.Shared.Next(100) >= 20)
            {
                LogPokemon(room, side, $"{subject} 얼어붙어서 움직일 수 없다!");
                return false;
            }
            user.Status = null;
            LogPokemon(room, side, $"{user.Info.Name}의 얼음이 녹았다!");
        }

        if (user.ConfusionTurns > 0)
        {
            if (--user.ConfusionTurns == 0)
                LogPokemon(room, side, $"{user.Info.Name}의 혼란이 풀렸다!");
            else
            {
                LogPokemon(room, side, $"{subject} 혼란에 빠져 있다!");
                // 5세대는 50% 확률로 자신을 공격한다.
                if (Random.Shared.Next(2) == 0)
                {
                    int damage = ConfusionDamage(user);
                    LogBoth(room, side, $"영문도 모른 채 자신을 공격했다! ({damage} 데미지)");
                    DealDamage(room, side, damage);
                    return false;
                }
            }
        }

        if (user.Infatuated)
        {
            LogPokemon(room, side, $"{subject} 헤롱헤롱해 있다!");
            if (Random.Shared.Next(2) == 0)
            {
                LogPokemon(room, side, $"{subject} 헤롱헤롱해서 기술을 쓸 수 없었다!");
                return false;
            }
        }

        if (user.Status == Paralysis && Random.Shared.Next(4) == 0)
        {
            LogPokemon(room, side, $"{subject} 몸이 저려서 움직일 수 없다!");
            return false;
        }
        return true;
    }

    // 혼란 자해: 위력 40의 타입 없는 물리 공격을 자신에게 한다.
    private static int ConfusionDamage(BattlePokemon pokemon)
    {
        int damage = (2 * BattlePokemon.Level / 5 + 2) * 40 * pokemon.Attack / pokemon.Defense / 50 + 2;
        return Math.Max(1, damage * Random.Shared.Next(85, 101) / 100);
    }

    // 양쪽 행동이 끝난 뒤: 회복(희망사항·아쿠아링·뿌리박기) → 화상·독·맹독 데미지 → 하품으로 잠들기
    // → 우리 편·필드 효과 턴 감소
    private void EndOfTurn(GameRoom room)
    {
        IEnumerable<BattleSide> sides = new[] { room.HostSide!, room.GuestSide! }
            .OrderByDescending(GetSpeed).ToList();

        ClearTurnProtection(room);          // 방어·버티기·매직코트·패스트가드는 이번 턴까지만
        foreach (BattleSide side in sides)
            side.Current.Roosting = false;   // 날개쉬기로 없어진 비행 타입은 이번 턴까지만
        EndOfTurnHealing(room, sides);

        foreach (BattleSide side in sides)
        {
            BattlePokemon pokemon = side.Current;
            if (pokemon.Hp == 0 || pokemon.Status is not (Burn or Poison or BadPoison))
                continue;

            // 화상·독은 최대 HP의 1/8, 맹독은 1/16, 2/16, 3/16 ... 으로 늘어난다.
            int damage = pokemon.Status == BadPoison
                ? pokemon.MaxHp * Math.Min(++pokemon.ToxicCounter, 15) / 16
                : pokemon.MaxHp / 8;
            damage = Math.Max(1, damage);
            string cause = pokemon.Status == Burn ? "화상" : "독";
            LogPokemon(room, side, $"{WithJosa(pokemon.Info.Name, "은", "는")} {cause} 데미지를 입었다! ({damage} 데미지)");
            DealDamage(room, side, damage);
        }

        foreach (BattleSide side in sides)
        {
            BattlePokemon pokemon = side.Current;
            if (pokemon.Hp > 0 && pokemon.DrowsyTurns > 0 && --pokemon.DrowsyTurns == 0)
                TryInflict(room, side, Sleep, announceFailure: false, fromOpponent: true);
        }

        TickSideEffects(room);
    }

    // HP를 줄이고, 쓰러지면 기록을 남긴 뒤 남은 포켓몬이 있으면 교체를 요구한다.
    private static void DealDamage(GameRoom room, BattleSide side, int damage)
    {
        BattlePokemon pokemon = side.Current;
        if (pokemon.Hp == 0)
            return;
        pokemon.Hp = Math.Max(0, pokemon.Hp - damage);
        if (pokemon.Hp > 0)
            return;

        LogPokemon(room, side, $"{WithJosa(pokemon.Info.Name, "이", "가")} 쓰러졌다!");
        if (side.Team.Any(member => member.Hp > 0))
            side.MustSwitch = true;
    }

    private double TypeMultiplier(PokemonMoveInfo move, BattlePokemon defender)
    {
        double multiplier = 1;
        foreach (int typeId in new[] { defender.Info.Type1Id, defender.Info.Type2Id ?? -1 })
        {
            // 날개쉬기를 쓴 턴에는 비행 타입이 없다. (비행 단일 타입이면 상성 1배)
            if (typeId < 0 || (defender.Roosting && HasTypeName(typeId, "비행")))
                continue;
            multiplier *= data.GetMultiplier(move.TypeId, typeId);
        }
        return multiplier;
    }

    private bool HasTypeName(int typeId, string name) => data.TypesById[typeId].Name == name;

    private bool HasType(BattlePokemon pokemon, string typeName) =>
        HasTypeName(pokemon.Info.Type1Id, typeName) ||
        (pokemon.Info.Type2Id is int type2 && HasTypeName(type2, typeName));

    private string StatusName(string code) => data.StatusConditionsById.Values.First(s => s.Code == code).Name;

    // owner 팀의 포켓몬 이야기: owner에게는 그대로, 상대에게는 "상대 "를 붙여 보낸다.
    private static void LogPokemon(GameRoom room, BattleSide owner, string text) =>
        Log(room, owner, text, "상대 " + text);

    private static void LogBoth(GameRoom room, BattleSide side, string text) => Log(room, side, text, text);
}
