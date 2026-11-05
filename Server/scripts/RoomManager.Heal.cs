// HP를 회복하는 변화 기술. 수치는 5세대 규칙을 따르며, 날씨가 없으므로 날씨에 따른 회복량 변화는 없다.
partial class RoomManager
{
    private const int RecoverMoveId = 105, SoftBoiledMoveId = 135, MilkDrinkMoveId = 208, PainSplitMoveId = 220,
        MorningSunMoveId = 234, SynthesisMoveId = 235, MoonlightMoveId = 236, SwallowMoveId = 256, WishMoveId = 273,
        IngrainMoveId = 275, SlackOffMoveId = 303, RoostMoveId = 355, AquaRingMoveId = 392, HealOrderMoveId = 456;

    // 최대 HP의 절반을 바로 회복하는 기술
    private static readonly HashSet<int> HalfHealMoveIds =
    [
        RecoverMoveId, SoftBoiledMoveId, MilkDrinkMoveId, MorningSunMoveId, SynthesisMoveId, MoonlightMoveId,
        SlackOffMoveId, RoostMoveId, HealOrderMoveId
    ];

    private static readonly HashSet<int> HealMoveIds =
        [.. HalfHealMoveIds, PainSplitMoveId, SwallowMoveId, WishMoveId, IngrainMoveId, AquaRingMoveId];

    private static void UseHealMove(GameRoom room, BattleSide attacker, BattleSide defender, PokemonMoveInfo move)
    {
        BattlePokemon user = attacker.Current, target = defender.Current;
        string subject = WithJosa(user.Info.Name, "은", "는");

        if (HalfHealMoveIds.Contains(move.Id))
        {
            if (!Heal(room, attacker, user.MaxHp / 2))
                LogBoth(room, attacker, "하지만 실패했다!");
            else if (move.Id == RoostMoveId)
                user.Roosting = true;   // 이번 턴이 끝날 때까지 비행 타입이 없다.
            return;
        }

        switch (move.Id)
        {
            case PainSplitMoveId:
                // 두 포켓몬의 HP를 평균으로 맞춘다.
                int average = (user.Hp + target.Hp) / 2;
                user.Hp = Math.Min(user.MaxHp, average);
                target.Hp = Math.Min(target.MaxHp, average);
                LogBoth(room, attacker, "서로의 아픔을 나눠 가졌다!");
                break;
            case SwallowMoveId:
                // 비축한 수만큼 회복(1개 1/4, 2개 1/2, 3개 전부)하고, 비축과 그로 오른 방어·특방을 되돌린다.
                if (user.StockpileCount == 0 || user.Hp == user.MaxHp)
                {
                    LogBoth(room, attacker, "하지만 실패했다!");
                    break;
                }
                int amount = user.StockpileCount switch { 1 => user.MaxHp / 4, 2 => user.MaxHp / 2, _ => user.MaxHp };
                Heal(room, attacker, amount);
                ChangeStage(room, attacker, Stat.Defense, -user.StockpileDefense, announceFailure: false);
                ChangeStage(room, attacker, Stat.SpDefense, -user.StockpileSpDefense, announceFailure: false);
                user.StockpileCount = user.StockpileDefense = user.StockpileSpDefense = 0;
                break;
            case WishMoveId:
                if (attacker.WishTurns > 0)
                {
                    LogBoth(room, attacker, "하지만 실패했다!");
                    break;
                }
                attacker.WishTurns = 2;
                attacker.WishHeal = user.MaxHp / 2;
                LogPokemon(room, attacker, $"{subject} 희망사항을 빌었다!");
                break;
            case IngrainMoveId:
                if (user.Ingrained)
                {
                    LogBoth(room, attacker, "하지만 실패했다!");
                    break;
                }
                user.Ingrained = true;
                LogPokemon(room, attacker, $"{subject} 뿌리를 박았다!");
                break;
            case AquaRingMoveId:
                if (user.AquaRing)
                {
                    LogBoth(room, attacker, "하지만 실패했다!");
                    break;
                }
                user.AquaRing = true;
                LogPokemon(room, attacker, $"{subject} 물의 베일을 둘렀다!");
                break;
        }
    }

    // HP를 회복하고 기록을 남긴다. 이미 가득 차 있으면 false.
    private static bool Heal(GameRoom room, BattleSide side, int amount)
    {
        BattlePokemon pokemon = side.Current;
        if (pokemon.Hp == 0 || pokemon.Hp == pokemon.MaxHp)
            return false;
        int healed = Math.Min(Math.Max(1, amount), pokemon.MaxHp - pokemon.Hp);
        pokemon.Hp += healed;
        LogPokemon(room, side, $"{WithJosa(pokemon.Info.Name, "은", "는")} 체력을 회복했다! (+{healed})");
        return true;
    }

    // 턴 끝 회복: 희망사항 → 아쿠아링 → 뿌리박기 (독·화상 데미지보다 먼저)
    private static void EndOfTurnHealing(GameRoom room, IEnumerable<BattleSide> sides)
    {
        foreach (BattleSide side in sides)
        {
            if (side.WishTurns > 0 && --side.WishTurns == 0)
            {
                LogSide(room, side, "{0}의 희망사항이 이루어졌다!");
                Heal(room, side, side.WishHeal);
            }
        }
        foreach (BattleSide side in sides)
            if (side.Current.AquaRing)
                Heal(room, side, side.Current.MaxHp / 16);
        foreach (BattleSide side in sides)
            if (side.Current.Ingrained)
                Heal(room, side, side.Current.MaxHp / 16);
    }
}
