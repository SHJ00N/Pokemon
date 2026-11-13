// 방어·보호 기술. 수치는 6세대 규칙을 따른다.
partial class RoomManager
{
    private const int ProtectMoveId = 182, DetectMoveId = 197, EndureMoveId = 203, SubstituteMoveId = 164,
        SafeguardMoveId = 219, MagicCoatMoveId = 277, QuickGuardMoveId = 501;
    private const int KingsShieldMoveId = 588, SpikyShieldMoveId = 596, MatBlockMoveId = 561, CraftyShieldMoveId = 578,
        WideGuardMoveId = 469;

    private static readonly HashSet<int> ProtectMoveIds =
    [
        ProtectMoveId, DetectMoveId, EndureMoveId, SubstituteMoveId, SafeguardMoveId, MagicCoatMoveId, QuickGuardMoveId,
        KingsShieldMoveId, SpikyShieldMoveId, MatBlockMoveId, CraftyShieldMoveId, WideGuardMoveId
    ];

    // 소리 기술 (6세대까지). 6세대부터 대타를 무시하고 포켓몬에게 직접 맞는다.
    // 울음소리, 울부짖기, 노래하기, 초음파, 싫은소리, 코골기, 멸망의노래, 치료방울, 소란피기, 하이퍼보이스,
    // 금속음, 풀피리, 벌레의야단법석, 수다, 돌림노래, 에코보이스, 옛노래, 바크아웃, 부르짖기, 차밍보이스,
    // 막말내뱉기, 폭음파, 비밀이야기
    private static readonly HashSet<int> SoundMoveIds =
    [
        45, 46, 47, 48, 103, 173, 195, 215, 253, 304, 319, 320, 405, 448, 496, 497, 547, 555, 568, 574, 575, 586, 590
    ];

    // 상대 포켓몬을 노리는 기술인지. 방어·판별·패스트가드에 막힌다. 변화 기술은 DB 플래그 protect도 본다.
    private bool TargetsOpponent(PokemonMoveInfo move) =>
        move.Category != "변화" || IsOpponentStatusMove(move) || HasFlag(move, "protect") ||
        move.Id is PowerSplitMoveId or GuardSplitMoveId or PowerSwapMoveId or GuardSwapMoveId
            or HeartSwapMoveId or PainSplitMoveId or MementoMoveId or PsychoShiftMoveId;

    // 상대에게 상태이상을 걸거나 상대 능력치를 바꾸는 변화 기술. 매직코트로 되받아칠 수 있고, 대타가 있으면 실패한다.
    private bool IsOpponentStatusMove(PokemonMoveInfo move) =>
        move.Category == "변화" &&
        (data.MoveStatusEffectsByMoveId[move.Id].Any(effect => effect.Target == "opponent") ||
         (TryGetStatChanges(move, out var changes) && changes.Any(change => change.Target == "opponent")) ||
         move.Id == DefogMoveId || TrapMoveIds.Contains(move.Id));

    // 매직코트로 되받아칠 수 있는지 (DB 플래그 reflectable 포함)
    private bool IsReflectable(PokemonMoveInfo move) =>
        IsOpponentStatusMove(move) || MagicCoatOnlyMoveIds.Contains(move.Id) || HasFlag(move, "reflectable");

    // 방어 계열로 막히는지: 방어·판별·니들가드(모두), 킹실드(공격 기술만), 패스트가드(우선도가 높은 기술),
    // 와이드가드(여러 대상을 노리는 공격), 마룻바닥세워막기(공격 기술), 트릭가드(변화 기술)
    private bool IsProtectedFrom(BattleSide defender, PokemonMoveInfo move)
    {
        BattlePokemon target = defender.Current;
        bool status = move.Category == "변화";
        string? spread = data.MoveMetaById.GetValueOrDefault(move.Id)?.Target;
        return (target.Protected && !(target.ProtectKind == "kings" && status)) ||
               (defender.QuickGuard && move.Priority > 0) ||
               (defender.WideGuard && !status && spread is "all-other-pokemon" or "all-opponents") ||
               (defender.MatBlock && !status) ||
               (defender.CraftyShield && status);
    }

    // 킹실드·니들가드로 막은 기술이 접촉 기술이면: 킹실드는 공격 -2, 니들가드는 최대 HP의 1/8 데미지
    private void ProtectContactEffect(GameRoom room, BattleSide attacker, BattleSide defender, PokemonMoveInfo move)
    {
        BattlePokemon user = attacker.Current, target = defender.Current;
        if (!target.Protected || !HasFlag(move, "contact"))
            return;
        if (target.ProtectKind == "kings")
        {
            int[] before = user.Stages.ToArray();
            ChangeStage(room, attacker, Stat.Attack, -2, announceFailure: false);
            SendStatEffects(room, attacker, user, before, user.FocusEnergy, TypeIdOf("강철"));
        }
        else if (target.ProtectKind == "spiky")
        {
            int damage = Math.Max(1, user.MaxHp / 8);
            LogPokemon(room, attacker, $"{WithJosa(user.Info.Name, "은", "는")} 니들가드에 찔렸다! ({damage} 데미지)");
            DealDamage(room, attacker, damage);
        }
    }

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
            case KingsShieldMoveId:
            case SpikyShieldMoveId:
            case WideGuardMoveId:
                // 그 턴에 마지막으로 행동하면 실패하고, 연속으로 쓰면 성공 확률이 1/3, 1/9 ... 로 줄어든다(6세대).
                // 패스트가드·와이드가드는 연속으로 써도 확률이 줄지 않지만, 쓰면 방어·판별·버티기의 연속 횟수는 늘어난다.
                bool chanceFails = move.Id is not (QuickGuardMoveId or WideGuardMoveId) &&
                    Random.Shared.NextDouble() >= Math.Pow(1.0 / 3, Math.Min(user.ProtectStreak, 6));
                if (movesLast || chanceFails)
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
                else if (move.Id == WideGuardMoveId)
                {
                    side.WideGuard = true;
                    LogSide(room, side, "{0}이 와이드가드로 보호받고 있다!");
                }
                else
                {
                    // 킹실드: 변화 기술은 막지 못하고, 막은 접촉 공격의 공격 -2. 니들가드: 막은 접촉 공격에 1/8 데미지
                    user.Protected = true;
                    user.ProtectKind = move.Id switch { KingsShieldMoveId => "kings", SpikyShieldMoveId => "spiky", _ => null };
                    LogPokemon(room, side, $"{subject} 방어 태세에 들어갔다!");
                }
                break;

            case MatBlockMoveId:
                // 나온 뒤 첫 행동일 때만, 그 턴의 공격 기술을 막는다.
                if (user.MoveActions > 1 || movesLast)
                {
                    LogBoth(room, side, "하지만 실패했다!");
                    return;
                }
                side.MatBlock = true;
                LogPokemon(room, side, $"{subject} 다다미를 세워 공격을 막으려 한다!");
                break;

            case CraftyShieldMoveId:
                // 그 턴에 상대의 변화 기술을 막는다.
                if (movesLast)
                {
                    LogBoth(room, side, "하지만 실패했다!");
                    return;
                }
                side.CraftyShield = true;
                LogSide(room, side, "{0}이 트릭가드로 보호받고 있다!");
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

    // 턴 끝: 이번 턴에만 유지되는 방어·버티기·매직코트·패스트가드·와이드가드·마룻바닥세워막기·트릭가드를 푼다.
    // 이번 턴에 방어 계열을 쓰지 않았으면 연속 횟수도 처음으로 돌린다.
    private static void ClearTurnProtection(GameRoom room)
    {
        foreach (BattleSide side in new[] { room.HostSide!, room.GuestSide! })
        {
            side.QuickGuard = side.WideGuard = side.MatBlock = side.CraftyShield = false;
            BattlePokemon pokemon = side.Current;
            pokemon.Protected = pokemon.Enduring = pokemon.MagicCoat = pokemon.Flinched = false;
            pokemon.ProtectKind = null;
            if (!pokemon.UsedProtectThisTurn)
                pokemon.ProtectStreak = 0;
            pokemon.UsedProtectThisTurn = false;
        }
    }
}
