// 공격기의 특수한 동작. 수치는 6세대 규칙을 따른다.
// 2턴 기술, 반동으로 쉬는 기술, 자폭, 빗나가면 반동을 받는 기술, 기습·힘껏펀치·비장의무기의 사용 조건, 미래예지·파멸의소원
partial class RoomManager
{
    private const int SelfDestructMoveId = 120, ExplosionMoveId = 153;     // 자폭·대폭발: 쓰면 자신이 쓰러진다.
    // 맞히면 다음 턴에 반동으로 움직이지 못한다: 파괴광선, 블라스트번, 하이드로캐논, 하드플랜트, 기가임팩트, 암석포, 시간의포효
    private static readonly HashSet<int> RechargeMoveIds = [63, 307, 308, 338, 416, 439, 459];
    // 빗나가거나 막히면(방어·타입 상성) 최대 HP의 1/2 반동: 점프킥, 무릎차기
    private static readonly HashSet<int> CrashMoveIds = [26, 136];
    private const int SuckerPunchMoveId = 389, FocusPunchMoveId = 264, LastResortMoveId = 387;
    // 마지막수단: 남은 PP가 적을수록 위력이 오르지만, PP가 없는 게임이라 PP가 충분할 때의 위력 40으로 쓴다.
    private const int TrumpCardMoveId = 376, TrumpCardPower = 40;
    private const int FutureSightMoveId = 248, DoomDesireMoveId = 353;
    private const int SkullBashMoveId = 130, SkyDropMoveId = 507, ShadowForceMoveId = 467, PhantomForceMoveId = 566;
    private const int FeintMoveId = 364, HyperspaceHoleMoveId = 593, HyperspaceFuryMoveId = 621, BrickBreakMoveId = 280,
        SmackDownMoveId = 479, UproarMoveId = 253, RageMoveId = 99, FalseSwipeMoveId = 206, FellStingerMoveId = 565;
    // 방어를 뚫는 기술: 섀도다이브, 고스트다이브, 페인트, 이차원홀, 이차원러시
    private static readonly HashSet<int> BreaksProtectMoveIds =
        [ShadowForceMoveId, PhantomForceMoveId, FeintMoveId, HyperspaceHoleMoveId, HyperspaceFuryMoveId];

    // 숨은 곳: 공중(공중날기·뛰어오르기·프리폴), 땅속(구멍파기), 물속(다이빙), 사라짐(섀도다이브·고스트다이브)
    private const string Air = "air", Underground = "underground", Underwater = "underwater", Vanished = "vanished";

    // 2턴 기술: 첫 턴에 남기는 기록({0} 이름, {1} 이름+은/는)과 숨는 곳. 첫 턴에는 공격하지 않는다.
    private static readonly Dictionary<int, (string Message, string? Hide)> TwoTurnMoves = new()
    {
        [13] = ("{0}의 주위에서 공기가 소용돌이친다!", null),   // 칼바람
        [76] = ("{1} 빛을 흡수했다!", null),                    // 솔라빔 (쾌청일 때 바로 쏘는 것은 날씨 구현 때)
        [130] = ("{1} 목을 움츠렸다!", null),                   // 로켓박치기: 첫 턴에 방어 +1
        [143] = ("{1} 강렬한 빛에 휩싸였다!", null),            // 불새
        [553] = ("{1} 차가운 빛에 둘러싸였다!", null),          // 프리즈볼트
        [554] = ("{1} 차가운 공기에 둘러싸였다!", null),        // 콜드플레어
        [19] = ("{1} 하늘 높이 날아올랐다!", Air),              // 공중날기
        [340] = ("{1} 높이 뛰어올랐다!", Air),                  // 뛰어오르기
        [91] = ("{1} 땅으로 파고들었다!", Underground),         // 구멍파기
        [291] = ("{1} 물속으로 숨었다!", Underwater),           // 다이빙
        [467] = ("{0}의 모습이 사라졌다!", Vanished),           // 섀도다이브: 공격하는 턴에 방어를 뚫는다.
        [566] = ("{0}의 모습이 사라졌다!", Vanished),           // 고스트다이브: 공격하는 턴에 방어를 뚫는다.
        [507] = ("", Air),                                      // 프리폴: 상대를 데리고 올라간다 (StartSkyDrop)
        [601] = ("{1} 힘을 모으고 있다!", null),                // 지오컨트롤: 다음 턴에 특수공격·특수방어·스피드 +2
    };

    // 숨은 상대에게도 맞는 기술. 공중: 바람일으키기·회오리·번개·스카이어퍼·폭풍·떨어뜨리기,
    // 땅속: 지진·매그니튜드·땅가르기, 물속: 파도타기·바다회오리. 사라진 상대에게는 아무것도 맞지 않는다.
    private static readonly Dictionary<string, HashSet<int>> HitsHiddenMoveIds = new()
    {
        [Air] = [16, 239, 87, 327, 542, 479], [Underground] = [89, 222, 90], [Underwater] = [57, 250], [Vanished] = [],
    };
    // 숨은 상대를 맞히면 위력 2배: 바람일으키기·회오리(공중), 지진·매그니튜드(땅속), 파도타기·바다회오리(물속)
    private static readonly HashSet<int> DoubleOnHiddenMoveIds = [16, 239, 89, 222, 57, 250];

    // 숨은 상대에게 맞는지. 독 타입이 쓰는 맹독은 숨은 상대에게도 맞는다(6세대).
    private bool HitsHiddenTarget(BattlePokemon user, PokemonMoveInfo move, string hidden) =>
        HitsHiddenMoveIds[hidden].Contains(move.Id) || (move.Id == ToxicMoveId && HasType(user, "독"));

    // 2턴 기술 첫 턴: 힘을 모으고(숨고), 다음 턴에 같은 기술로 자동으로 공격한다(LOCKED).
    private static void StartCharging(GameRoom room, BattleSide side, PokemonMoveInfo move, int slot)
    {
        BattlePokemon user = side.Current;
        var (message, hide) = TwoTurnMoves[move.Id];
        LogPokemon(room, side, $"{user.Info.Name}의 {move.Name}!");
        LogPokemon(room, side, string.Format(message, user.Info.Name, WithJosa(user.Info.Name, "은", "는")));
        user.ChargingSlot = slot;
        user.ChargingMove = move;   // 손가락흔들기 등으로 나온 기술도 다음 턴에 그대로 쓴다.
        user.Hidden = hide;
        if (move.Id == SkullBashMoveId)
        {
            int[] before = user.Stages.ToArray();
            ChangeStage(room, side, Stat.Defense, 1, announceFailure: false);
            SendStatEffects(room, side, user, before, user.FocusEnergy, move.TypeId);
        }
    }

    // 프리폴 첫 턴: 상대를 데리고 하늘로 올라간다. 둘 다 공중에 숨고, 상대는 다음 턴에 떨어질 때까지 행동·교체할 수 없다.
    // 상대에게 대타가 있거나 200kg 이상이면 실패한다.
    private static void StartSkyDrop(GameRoom room, BattleSide attacker, BattleSide defender, PokemonMoveInfo move, int slot)
    {
        BattlePokemon user = attacker.Current, target = defender.Current;
        LogPokemon(room, attacker, $"{user.Info.Name}의 {move.Name}!");
        if (target.SubstituteHp > 0 || target.Info.Weight >= 2000)
        {
            LogBoth(room, attacker, "하지만 실패했다!");
            return;
        }
        if (!AccuracyCheck(room, user, target, move))
        {
            LogBoth(room, attacker, "하지만 빗나갔다!");
            return;
        }
        user.ChargingSlot = slot;
        user.ChargingMove = move;
        user.Hidden = target.Hidden = Air;
        target.SkyDropped = true;
        string subject = WithJosa(user.Info.Name, "은", "는"), carried = WithJosa(target.Info.Name, "을", "를");
        Log(room, attacker, $"{subject} 상대 {carried} 하늘로 데려갔다!", $"상대 {subject} {carried} 하늘로 데려갔다!");
    }

    // 2턴 기술이 끝나거나(공격하는 턴) 취소되면(행동하지 못함) 숨은 상태를 풀고, 프리폴로 데려간 상대도 풀어 준다.
    private static void EndCharging(BattleSide attacker, BattleSide defender)
    {
        BattlePokemon user = attacker.Current;
        if (user.ChargingSlot is int slot && user.Moves[slot - 1].Id == SkyDropMoveId && defender.Current.SkyDropped)
        {
            defender.Current.SkyDropped = false;
            defender.Current.Hidden = null;
        }
        user.ChargingSlot = null;
        user.ChargingMove = null;
        user.Hidden = null;
    }

    // 턴 끝: 프리폴을 쓴 포켓몬이 쓰러지는 등으로 더 이상 붙잡고 있지 않으면 붙잡힌 포켓몬을 풀어 준다.
    private static void ReleaseSkyDrop(GameRoom room)
    {
        foreach (BattleSide side in new[] { room.HostSide!, room.GuestSide! })
        {
            BattlePokemon holder = GetOther(room, side).Current;
            bool held = holder.Hp > 0 && holder.ChargingSlot is int slot && holder.Moves[slot - 1].Id == SkyDropMoveId;
            if (side.Current.SkyDropped && !held)
            {
                side.Current.SkyDropped = false;
                side.Current.Hidden = null;
            }
        }
    }

    // 숨은 상대를 노렸다가 빗나감
    private void LogMissHidden(GameRoom room, BattleSide attacker, PokemonMoveInfo move)
    {
        string head = $"{attacker.Current.Info.Name}의 {move.Name}!";
        if (move.Category == "변화")
        {
            LogPokemon(room, attacker, head);
            LogBoth(room, attacker, "하지만 빗나갔다!");
            return;
        }
        SendMoveEffect(room, attacker, move, 0);
        LogPokemon(room, attacker, head + " 공격이 빗나갔다!");
    }

    // 섀도다이브·고스트다이브·페인트·이차원홀·이차원러시: 상대의 방어(방어·판별·킹실드·니들가드·패스트가드·와이드가드·
    // 마룻바닥세워막기·트릭가드)를 뚫고 공격하며 그 턴의 방어를 없앤다.
    private static void BreakProtection(GameRoom room, BattleSide defender, PokemonMoveInfo move)
    {
        BattlePokemon target = defender.Current;
        bool guarded = target.Protected || defender.QuickGuard || defender.WideGuard || defender.MatBlock || defender.CraftyShield;
        if (!BreaksProtectMoveIds.Contains(move.Id) || !guarded)
            return;
        target.Protected = false;
        target.ProtectKind = null;
        defender.QuickGuard = defender.WideGuard = defender.MatBlock = defender.CraftyShield = false;
        LogPokemon(room, defender, $"{target.Info.Name}의 방어가 풀렸다!");
    }

    // 깨뜨리다: 공격하기 전에 상대 편의 리플렉터·빛의장막을 없앤다(효과가 없는 상대에게는 없애지 않는다).
    private void BreakScreens(GameRoom room, BattleSide defender, PokemonMoveInfo move)
    {
        if (move.Id != BrickBreakMoveId || defender.ReflectTurns + defender.LightScreenTurns == 0 ||
            TypeMultiplier(move, defender.Current, room) == 0)
            return;
        defender.ReflectTurns = defender.LightScreenTurns = 0;
        LogSide(room, defender, "{0}의 리플렉터·빛의장막이 깨졌다!");
    }

    // 떨어뜨리기: 맞은 상대가 땅에 떨어진다(비행 타입·전자부유·텔레키네시스도 땅 기술을 맞음). 공중날기·뛰어오르기 중이면 취소된다.
    private void SmackDown(GameRoom room, BattleSide attacker, BattleSide defender)
    {
        BattlePokemon target = defender.Current;
        bool airborne = HasType(target, "비행") || target.MagnetRiseTurns > 0 || target.TelekinesisTurns > 0 ||
                        (target.Hidden == Air && !target.SkyDropped);
        if (target.Hidden == Air && !target.SkyDropped)
            EndCharging(defender, attacker);
        target.SmackedDown = true;
        target.MagnetRiseTurns = target.TelekinesisTurns = 0;
        if (airborne)
            LogPokemon(room, defender, $"{WithJosa(target.Info.Name, "은", "는")} 땅으로 떨어졌다!");
    }

    // 소란피기: 쓰면 잠든 포켓몬이 모두 깨어나고, 맞히면 3턴 동안 이어서 쓴다(LOCKED). 그동안 아무도 잠들 수 없다.
    private static void Uproar(GameRoom room, BattleSide side, bool landed, bool called, int slot)
    {
        BattlePokemon user = side.Current;
        foreach (BattleSide each in new[] { room.HostSide!, room.GuestSide! })
        {
            if (each.Current.Hp > 0 && each.Current.Status == Sleep)
            {
                each.Current.Status = null;
                LogPokemon(room, each, $"{WithJosa(each.Current.Info.Name, "은", "는")} 소란 때문에 잠에서 깼다!");
            }
        }
        if (user.UproarTurns == 0 && landed && !called)
        {
            user.UproarTurns = 3;
            LogPokemon(room, side, $"{WithJosa(user.Info.Name, "은", "는")} 소란을 피우기 시작했다!");
        }
        if (user.UproarTurns > 0 && --user.UproarTurns == 0)
            LogPokemon(room, side, $"{WithJosa(user.Info.Name, "은", "는")} 소란을 멈췄다!");
        user.LockedSlot = user.UproarTurns > 0 ? slot : null;
    }

    // 소란 중인 포켓몬이 있으면 아무도 잠들 수 없다.
    private static bool UproarActive(GameRoom room) =>
        room.HostSide!.Current.UproarTurns > 0 || room.GuestSide!.Current.UproarTurns > 0;

    // 점프킥·무릎차기가 빗나가거나 막히면 최대 HP의 1/2만큼 반동을 받는다.
    private static void Crash(GameRoom room, BattleSide side)
    {
        BattlePokemon user = side.Current;
        int damage = Math.Max(1, user.MaxHp / 2);
        LogPokemon(room, side, $"{WithJosa(user.Info.Name, "은", "는")} 의욕이 넘쳐서 땅에 부딪혔다! ({damage} 데미지)");
        DealDamage(room, side, damage);
    }

    // 기습: 상대가 이번 턴에 아직 행동하지 않았고 공격 기술을 고른 경우에만 성공한다.
    private static bool WillAttack(BattleSide side)
    {
        BattlePokemon pokemon = side.Current;
        return side.ChosenMove is int slot && !pokemon.MovedThisTurn && pokemon.RechargeSlot == null &&
               !pokemon.SkyDropped && pokemon.Moves[slot - 1].Category != "변화";
    }

    // 비장의무기: 자신의 다른 기술을 나온 뒤 모두 한 번 이상 써야 성공한다. 비장의무기만 알고 있으면 실패한다.
    private static bool CanUseLastResort(BattlePokemon user) =>
        user.Moves.Length > 1 && user.Moves.All(move => move.Id == LastResortMoveId || user.UsedMoveIds.Contains(move.Id));

    // 미래예지·파멸의소원: 2턴 뒤 턴이 끝날 때 그 자리의 포켓몬이 맞는다. 이미 예약돼 있으면 실패한다.
    // 방어로 막을 수 없다.
    private static void UseFutureAttack(GameRoom room, BattleSide attacker, BattleSide defender, PokemonMoveInfo move)
    {
        BattlePokemon user = attacker.Current;
        string subject = WithJosa(user.Info.Name, "은", "는");
        LogPokemon(room, attacker, $"{user.Info.Name}의 {move.Name}!");
        if (defender.FutureAttackTurns > 0)
        {
            LogBoth(room, attacker, "하지만 실패했다!");
            return;
        }
        defender.FutureAttackTurns = 3;     // 이번 턴 끝 → 2, 다음 턴 끝 → 1, 그다음 턴 끝 → 0에 맞는다.
        defender.FutureAttackUser = user;
        defender.FutureAttackMove = move;
        LogPokemon(room, attacker, move.Id == FutureSightMoveId ? $"{subject} 미래를 내다보았다!" : $"{subject} 파멸의 소원을 미래에 맡겼다!");
    }

    // 턴 끝: 예약된 미래예지·파멸의소원이 맞는다. 데미지는 맞는 순간 계산한다(5세대부터). 쓴 포켓몬이 교체되거나
    // 쓰러졌어도 그 포켓몬의 능력치로 계산한다.
    private void TickFutureAttacks(GameRoom room)
    {
        foreach (BattleSide side in new[] { room.HostSide!, room.GuestSide! })
        {
            if (side.FutureAttackTurns == 0 || --side.FutureAttackTurns > 0)
                continue;
            BattlePokemon user = side.FutureAttackUser!, target = side.Current;
            PokemonMoveInfo move = side.FutureAttackMove!;
            side.FutureAttackUser = null;
            side.FutureAttackMove = null;
            if (target.Hp == 0)
                continue;

            BattleSide userSide = GetOther(room, side);
            var result = CalculateDamage(room, userSide, side, move, user: user);
            bool hit = result.Hit && result.Effectiveness > 0;
            SendMoveEffect(room, userSide, move, hit ? result.Effectiveness : 0);
            string text = $"{WithJosa(target.Info.Name, "은", "는")} {move.Name} 공격을 받았다!";
            if (!result.Hit)
                text = $"{move.Name} 공격은 {target.Info.Name}에게 빗나갔다!";
            else if (result.Effectiveness == 0)
                text = $"{target.Info.Name}에게는 {move.Name}의 효과가 없는 것 같다...";
            else
                text += (result.Critical ? " 급소에 맞았다!" : "") +
                        (result.Effectiveness > 1 ? " 효과가 굉장했다!" : result.Effectiveness < 1 ? " 효과가 별로인 듯하다..." : "") +
                        $" ({result.Damage} 데미지)";
            LogPokemon(room, side, text);
            if (!hit)
                continue;

            var (_, body, endured) = ApplyHit(room, side, move, result.Damage);
            if (endured)
                LogPokemon(room, side, $"{WithJosa(target.Info.Name, "은", "는")} 공격을 버텼다!");
            if (body && target.Hp == 0)
                Faint(room, side);
            SendBattleState(room);
        }
    }

    // 공격 한 번을 맞힌다. 대타가 있으면 대타가 대신 맞고(소리 기술·이차원홀 등은 대타를 무시, 6세대),
    // 버티기 중이면 HP 1로 버틴다. 칼등치기는 HP를 1 남긴다. 분노 중이면 맞을 때마다 공격 +1
    // 쓰러졌을 때의 처리(Faint)는 부른 쪽에서 한다. Dealt: 대타를 포함해 실제로 준 데미지, Body: 포켓몬 본체에 맞았는지
    private (int Dealt, bool Body, bool Endured) ApplyHit(GameRoom room, BattleSide defender,
        PokemonMoveInfo move, int damage)
    {
        BattlePokemon target = defender.Current;
        int substituteHp = target.SubstituteHp;
        if (!BypassesSubstitute(move) && HitSubstitute(room, defender, damage))
            return (substituteHp - target.SubstituteHp, false, false);
        int dealt = Math.Min(damage, target.Hp);
        bool endured = target.Enduring && dealt == target.Hp;
        if (endured || (move.Id == FalseSwipeMoveId && dealt == target.Hp))
            dealt--;
        target.Hp -= dealt;
        if (target.Raging && target.Hp > 0)
            ChangeStage(room, defender, Stat.Attack, 1, announceFailure: false);
        target.HitThisTurn = true;
        target.HurtThisTurn |= dealt > 0;
        // 카운터·미러코트·메탈버스트·참기가 되돌려 줄 데미지
        if (move.Category == "물리")
            target.PhysicalDamageTaken = dealt;
        else
            target.SpecialDamageTaken = dealt;
        if (target.BideTurns > 0)
            target.BideDamage += dealt;
        return (dealt, true, endured);
    }
}
