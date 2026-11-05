// 방어·보호 기술. 수치는 5세대 규칙을 따른다.
partial class RoomManager
{
    private const int ProtectMoveId = 182, DetectMoveId = 197, EndureMoveId = 203, SubstituteMoveId = 164,
        SafeguardMoveId = 219, MagicCoatMoveId = 277, QuickGuardMoveId = 501;

    private static readonly HashSet<int> ProtectMoveIds =
    [
        ProtectMoveId, DetectMoveId, EndureMoveId, SubstituteMoveId, SafeguardMoveId, MagicCoatMoveId, QuickGuardMoveId
    ];

    // 상대 포켓몬을 노리는 기술인지. 방어·판별·패스트가드에 막힌다.
    private bool TargetsOpponent(PokemonMoveInfo move) =>
        move.Category != "변화" || IsOpponentStatusMove(move) ||
        move.Id is PowerSplitMoveId or GuardSplitMoveId or PowerSwapMoveId or GuardSwapMoveId
            or HeartSwapMoveId or PainSplitMoveId or MementoMoveId;

    // 상대에게 상태이상을 걸거나 상대 능력치를 바꾸는 변화 기술. 매직코트로 되받아칠 수 있고, 대타가 있으면 실패한다.
    private bool IsOpponentStatusMove(PokemonMoveInfo move) =>
        move.Category == "변화" &&
        (data.MoveStatusEffectsByMoveId[move.Id].Any(effect => effect.Target == "opponent") ||
         (StatChangeMoves.TryGetValue(move.Id, out var entry) && !entry.Self) ||
         move.Id == DefogMoveId);

    // 방어·판별, 또는 패스트가드(우선도가 높은 기술만)로 막히는지
    private static bool IsProtectedFrom(BattleSide defender, PokemonMoveInfo move) =>
        defender.Current.Protected || (defender.QuickGuard && move.Priority > 0);

    private static void UseProtectMove(GameRoom room, BattleSide side, PokemonMoveInfo move, bool movesLast)
    {
        BattlePokemon user = side.Current;
        string subject = WithJosa(user.Info.Name, "은", "는");
        switch (move.Id)
        {
            case ProtectMoveId:
            case DetectMoveId:
            case EndureMoveId:
            case QuickGuardMoveId:
                // 그 턴에 마지막으로 행동하면 실패하고, 연속으로 쓰면 성공 확률이 1/2, 1/4 ... 로 줄어든다.
                if (movesLast || Random.Shared.NextDouble() >= 1.0 / (1 << Math.Min(user.ProtectStreak, 8)))
                {
                    user.ProtectStreak = 0;
                    LogBoth(room, side, "하지만 실패했다!");
                    return;
                }
                user.ProtectStreak++;
                user.UsedProtectThisTurn = true;
                if (move.Id == EndureMoveId)
                {
                    user.Enduring = true;
                    LogPokemon(room, side, $"{subject} 버티기 태세에 들어갔다!");
                }
                else if (move.Id == QuickGuardMoveId)
                {
                    side.QuickGuard = true;
                    LogSide(room, side, "{0}이 패스트가드로 보호받고 있다!");
                }
                else
                {
                    user.Protected = true;
                    LogPokemon(room, side, $"{subject} 방어 태세에 들어갔다!");
                }
                break;

            case MagicCoatMoveId:
                if (movesLast)
                {
                    LogBoth(room, side, "하지만 실패했다!");
                    return;
                }
                user.MagicCoat = true;
                LogPokemon(room, side, $"{subject} 매직코트로 몸을 감쌌다!");
                break;

            case SubstituteMoveId:
                // 최대 HP의 1/4을 써서 같은 HP의 대타를 만든다.
                if (user.SubstituteHp > 0)
                    LogBoth(room, side, "하지만 이미 대타가 나와 있다!");
                else if (user.Hp <= user.MaxHp / 4)
                    LogBoth(room, side, "하지만 대타를 만들 체력이 부족하다!");
                else
                {
                    user.Hp -= user.MaxHp / 4;
                    user.SubstituteHp = user.MaxHp / 4;
                    LogPokemon(room, side, $"{user.Info.Name}의 대타가 나타났다!");
                }
                break;

            case SafeguardMoveId:
                StartSideEffect(room, side, side.SafeguardTurns, 5, value => side.SafeguardTurns = value,
                    "{0}이 신비의 베일에 둘러싸였다!");
                break;
        }
    }

    // 대타가 있으면 대타가 데미지를 대신 받는다. 대타가 받았으면 true (이때 추가 효과는 없다)
    private static bool HitSubstitute(GameRoom room, BattleSide defender, int damage)
    {
        BattlePokemon target = defender.Current;
        if (target.SubstituteHp == 0)
            return false;
        target.SubstituteHp = Math.Max(0, target.SubstituteHp - damage);
        LogBoth(room, defender, "대타가 공격을 대신 받았다!");
        if (target.SubstituteHp == 0)
            LogPokemon(room, defender, $"{target.Info.Name}의 대타는 사라졌다!");
        return true;
    }

    // 턴 끝: 이번 턴에만 유지되는 방어·버티기·매직코트·패스트가드를 푼다.
    // 이번 턴에 방어 계열을 쓰지 않았으면 연속 횟수도 처음으로 돌린다.
    private static void ClearTurnProtection(GameRoom room)
    {
        foreach (BattleSide side in new[] { room.HostSide!, room.GuestSide! })
        {
            side.QuickGuard = false;
            BattlePokemon pokemon = side.Current;
            pokemon.Protected = pokemon.Enduring = pokemon.MagicCoat = false;
            if (!pokemon.UsedProtectThisTurn)
                pokemon.ProtectStreak = 0;
            pokemon.UsedProtectThisTurn = false;
        }
    }
}
