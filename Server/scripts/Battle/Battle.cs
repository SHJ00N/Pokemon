using Pokemon.Protocol;

// 전투 하나의 상태와 규칙. 방·접속을 모르고, 메시지는 Send로 그 편을 보는 사람에게만 보낸다.
// 기술 효과는 Battle.*.cs에 나누어 둔다.
sealed partial class Battle
{
    private readonly GameData data;
    public BattleSide HostSide { get; }
    public BattleSide GuestSide { get; }
    public Random Random { get; }                   // 방의 무작위 (--seed면 고정)
    public BattleOutcome? Outcome { get; private set; }   // CheckEnd가 Over를 돌려준 뒤에만 값이 있다

    public int TrickRoomTurns { get; set; }  // 트릭룸 남은 턴: 스피드가 느린 쪽이 먼저 행동
    // 날씨·필드 (Battle.Field.cs). 남은 턴이 0이면 없다.
    public string? Weather { get; set; }        // rain, sun, sand, hail
    public int WeatherTurns { get; set; }
    public string? Terrain { get; set; }        // grassy, misty, electric
    public int TerrainTurns { get; set; }
    public int GravityTurns { get; set; }       // 중력
    public int WonderRoomTurns { get; set; }    // 원더룸: 방어와 특수방어가 바뀐다.
    public int MudSportTurns { get; set; }      // 흙놀이: 전기 기술 위력 1/3
    public int WaterSportTurns { get; set; }    // 물놀이: 불꽃 기술 위력 1/3
    public int FairyLockTurns { get; set; }     // 페어리록: 교체할 수 없다.
    public bool IonDeluge { get; set; }         // 플라스마샤워: 이번 턴에 노말 기술이 전기 타입이 된다.
    // 이번 턴에 아직 기술을 쓰지 않은 쪽 (행동 순서). 턴 도중 교체를 기다리는 동안 남아 있다가 이어서 처리한다.
    public List<BattleSide> TurnQueue { get; } = new();
    public int TurnNumber { get; set; }         // 전투 시작부터 센 턴 번호
    public int LastMoveIdThisTurn { get; set; } // 이번 턴에 마지막으로 성공한 기술 (크로스플레임·크로스썬더)
    public PokemonMoveInfo? LastExecutedMove { get; set; }  // 전투에서 마지막으로 나온 기술 (흉내쟁이)
    public int EchoedVoiceTurn { get; set; }    // 에코보이스를 마지막으로 쓴 턴과 이어서 쓴 횟수 (1~5)
    public int EchoedVoiceCount { get; set; }

    public Battle(GameData data, Random random, BattleSide host, BattleSide guest)
    {
        this.data = data;
        Random = random;
        HostSide = host;
        GuestSide = guest;
    }

    // 이 편을 보는 플레이어에게 보낸다.
    private void Send(BattleSide side, string message) => side.Player.QueueMessage(message);

    // 새 턴을 시작한다. 난동 기술·2턴 기술의 공격 턴·반동으로 쉬는 턴인 포켓몬은 행동을 자동으로 정한다(LOCKED).
    public void BeginTurn()
    {
        if (HostSide.MustSwitch || GuestSide.MustSwitch)
            return;
        foreach (BattleSide side in new[] { HostSide, GuestSide })
        {
            BattlePokemon pokemon = side.Current;
            if ((pokemon.LockedSlot ?? pokemon.ChargingSlot ?? pokemon.RechargeSlot) is int slot)
                side.ChosenMove = slot;
        }
    }

    public bool AllChosen => HostSide.HasChosen && GuestSide.HasChosen;

    // 한쪽(또는 양쪽)의 포켓몬이 모두 쓰러졌으면 승패를 정한다.
    // 턴 끝 데미지 등으로 양쪽 마지막 포켓몬이 함께 쓰러지면 무승부다.
    public TurnProgress CheckEnd()
    {
        bool hostAlive = HostSide.Team.Any(pokemon => pokemon.Hp > 0);
        bool guestAlive = GuestSide.Team.Any(pokemon => pokemon.Hp > 0);
        if (hostAlive && guestAlive)
            return TurnProgress.NextTurn;
        Outcome = hostAlive ? BattleOutcome.HostWins : guestAlive ? BattleOutcome.GuestWins : BattleOutcome.Draw;
        return TurnProgress.Over;
    }

    private void ApplyChosenMegaEvolution(BattleSide side)
    {
        if (side.ChosenMegaForm is not PokemonInfo form || !GetMegaForms(side).Contains(form))
            return;

        side.ChosenMegaForm = null;
        string name = side.Current.Info.Name;
        side.Current.MegaEvolve(form);
        side.HasMegaEvolved = true;
        SendBattleState(megaEvolved: side);
        LogPokemon(side, $"{WithJosa(name, "이", "가")} {WithJosa(form.Name, "으로", "로")} 메가진화했다!");
    }

    public IReadOnlyList<PokemonInfo> GetMegaForms(BattleSide side)
    {
        BattlePokemon pokemon = side.Current;
        // 레쿠쟈는 화룡점정을 알고 있어야 메가진화한다(6세대 ORAS).
        bool knowsRequiredMove = !data.MegaRequiredMoveByBaseId.TryGetValue(pokemon.Info.Id, out int requiredMove) ||
            pokemon.Moves.Any(slot => slot.Move.Id == requiredMove);
        return !side.HasMegaEvolved && pokemon.Hp > 0 && !pokemon.Transformed && knowsRequiredMove
            ? data.MegaFormsByBaseId.GetValueOrDefault(pokemon.Info.Id) ?? [] : [];
    }

    // 양쪽이 모두 행동을 고른 뒤(AllChosen) 한 턴을 처리한다. 교체 → 공격(우선도, 스피드 순) → 턴 끝 처리 순서다.
    public TurnProgress ResolveTurn()
    {
        BattleSide host = HostSide, guest = GuestSide;
        TurnNumber++;
        LastMoveIdThisTurn = 0;
        foreach (BattleSide side in new[] { host, guest })
        {
            side.FaintedLastTurn = side.FaintedThisTurn;
            side.FaintedThisTurn = false;
            side.TurnActor = side.Current;
            side.Current.ClearTurnRecord();
        }

        // 따라가때리기: 상대가 교체하려고 하면 교체하기 전에 위력 2배로 공격한다. 쓰러지면 교체하지 않고 턴 끝에 다시 고른다.
        var pursued = new HashSet<BattleSide>();
        foreach (BattleSide side in new[] { host, guest })
        {
            BattleSide pursuer = GetOther(side);
            if (side.ChosenSwitch == null || pursuer.ChosenMove is not int slot || pursuer.Struggling ||
                pursuer.Current.Moves[slot - 1].Move.Id != PursuitMoveId || pursuer.Current.RechargeSlot != null)
                continue;
            side.Current.SwitchingOut = true;
            ApplyChosenMegaEvolution(pursuer);   // 메가진화를 골랐으면 따라가때리기보다 먼저 메가진화한다.
            ExecuteMove(pursuer, side, movesLast: false);
            side.Current.SwitchingOut = false;
            pursued.Add(pursuer);
            SendBattleState();
            if (side.Current.Hp == 0)
                side.ChosenSwitch = null;
        }

        foreach (BattleSide side in new[] { host, guest })
        {
            if (side.ChosenSwitch is not int index)
                continue;
            string previous = side.Current.Info.Name;
            ChangeActive(side, index);
            string next = side.Current.Info.Name;
            Log(side, $"돌아와, {previous}! 가라, {next}!",
                $"상대가 {WithJosa(previous, "을", "를")} 거두고 {WithJosa(next, "을", "를")} 내보냈다!");
            ApplyEntryHazards(side);
            SendBattleState();
        }

        // 우선도가 높은 기술 → 스피드가 빠른 포켓몬(트릭룸이면 느린 포켓몬) → 같으면 무작위
        List<BattleSide> attackers = new[] { host, guest }
            .Where(side => side.ChosenMove != null && !pursued.Contains(side))
            .OrderByDescending(side => side.Struggling ? 0 : side.Current.Moves[side.ChosenMove!.Value - 1].Move.Priority)
            .ThenByDescending(side => TrickRoomTurns > 0 ? -GetSpeed(side) : GetSpeed(side))
            .ThenBy(_ => Random.Next())
            .ToList();

        foreach (BattleSide side in new[] { host, guest })
            side.TurnActor = side.Current;     // 교체해서 나온 포켓몬 (이번 턴의 기록은 교체할 때 지워졌다)
        // 힘껏펀치: 턴이 시작될 때 기합을 모은다. 기술을 쓰기 전에 공격을 맞으면 실패한다.
        foreach (BattleSide side in attackers)
        {
            BattlePokemon pokemon = side.Current;
            if (!side.Struggling && pokemon.Moves[side.ChosenMove!.Value - 1].Move.Id == FocusPunchMoveId &&
                pokemon.RechargeSlot == null && !pokemon.SkyDropped)
                LogPokemon(side, $"{WithJosa(pokemon.Info.Name, "은", "는")} 기합을 모으기 시작했다!");
        }

        // 메가진화 (6세대): 교체가 끝난 뒤, 어느 기술보다도 먼저 빠른 포켓몬부터 메가진화한다.
        // 그래서 상대가 먼저 공격해도 메가진화한 모습으로 맞는다. 행동 순서는 위에서 메가진화 전 스피드로 이미 정했다.
        foreach (BattleSide side in new[] { host, guest }
                     .OrderByDescending(side => TrickRoomTurns > 0 ? -GetSpeed(side) : GetSpeed(side)).ToArray())
            ApplyChosenMegaEvolution(side);

        TurnQueue.Clear();
        TurnQueue.AddRange(attackers);
        return ContinueTurn();
    }

    // 남은 행동을 순서대로 처리하고 턴을 끝낸다. 유턴·배턴터치 등으로 턴 도중에 교체할 포켓몬을 골라야 하면
    // 그 자리에서 멈추고(AwaitingMidTurnSwitch), 고르면 CompleteMidTurnSwitch가 다시 부른다.
    private TurnProgress ContinueTurn()
    {
        BattleSide host = HostSide, guest = GuestSide;
        while (TurnQueue.Count > 0)
        {
            BattleSide side = TurnQueue[0];
            TurnQueue.RemoveAt(0);
            // 드래곤테일 등으로 강제 교체되어 나온 포켓몬은 이번 턴에 행동하지 않는다.
            if (!ReferenceEquals(side.Current, side.TurnActor))
                continue;
            ExecuteMove(side, GetOther(side), movesLast: TurnQueue.Count == 0);
            // 각 공격의 HP·상태·능력치 결과를 다음 공격 효과보다 먼저 보낸다.
            SendBattleState();
            if (side.SwitchingMidTurn)
                return TurnProgress.AwaitingMidTurnSwitch;
        }

        EndOfTurn();

        host.ChosenMove = guest.ChosenMove = null;
        host.ChosenMegaForm = guest.ChosenMegaForm = null;
        host.ChosenSwitch = guest.ChosenSwitch = null;
        host.Struggling = guest.Struggling = false;
        SendBattleState();
        return CheckEnd();
    }

    // 출전 포켓몬을 바꾼다. 나가는 포켓몬의 일시 상태와, 그 포켓몬 때문에 걸린 상대의 헤롱헤롱이 풀린다.
    private void ChangeActive(BattleSide side, int index)
    {
        side.Current.OnSwitchOut();
        GetOther(side).Current.Infatuated = false;
        side.Active = index;
        side.Current.OnSwitchIn();
    }

    // 6세대 데미지 공식 (특성, 아이템은 반영하지 않음). HP는 바꾸지 않는다.
    // hit: 연속기의 몇 번째 타격인지. 명중 판정은 첫 타에서만 하고, 트리플킥은 매번 판정하며 위력이 10·20·30으로 오른다.
    // user: 공격하는 포켓몬 (미래예지처럼 지금 나와 있지 않은 포켓몬의 공격일 때만 준다)
    private (bool Supported, bool Hit, int Damage, double Effectiveness, bool Critical) CalculateDamage(
        BattleSide attackerSide, BattleSide defenderSide, PokemonMoveInfo move, int hit = 1, BattlePokemon? user = null)
    {
        BattlePokemon attacker = user ?? attackerSide.Current, defender = defenderSide.Current;
        // 변화 기술은 데미지가 없다.
        if (move.Category == "변화")
            return (false, true, 0, 1, false);

        // 정해진 데미지를 주는 기술 (일격필살·지구던지기·카운터 등): 효과가 없는지만 본다.
        // 일격필살은 명중률 30으로 랭크에 영향을 받지 않는다(같은 레벨).
        if (FixedDamage(attacker, defender, move) is int fixedDamage)
        {
            bool ohko = OhkoMoveIds.Contains(move.Id);
            bool lockedOn = ReferenceEquals(attacker.LockOnTarget, defender);
            if (ohko ? !lockedOn && Random.Next(100) >= 30 : !AccuracyCheck(attacker, defender, move))
                return (true, false, 0, 1, false);
            return TypeMultiplier(move, defender) == 0
                ? (true, true, 0, 0, false)
                : (true, true, Math.Max(1, fixedDamage), 1, false);
        }

        if (BasePower(attackerSide, defenderSide, attacker, move, hit) is not int power)
            return (false, true, 0, 1, false);

        bool tripleKick = move.Id == TripleKickMoveId;
        if ((hit == 1 || tripleKick) &&
            !AccuracyCheck(attacker, defender, move, ignoreEvasion: IgnoreStageMoveIds.Contains(move.Id)))
            return (true, false, 0, 1, false);
        if (tripleKick)
            power *= hit;

        // 발버둥은 타입이 없어 상성 1배, 자속 보정도 없다.
        double effectiveness = move.Id == StruggleMoveId ? 1 : TypeMultiplier(move, defender);
        // 플라잉프레스: 격투와 비행 상성을 곱한다. 싱크로노이즈: 같은 타입이 없으면 효과가 없다.
        if (move.Id == FlyingPressMoveId)
            effectiveness *= TypeMultiplier(move with { TypeId = TypeIdOf("비행") }, defender);
        if (move.Id == SynchronoiseMoveId && !SharesType(attacker, defender))
            effectiveness = 0;
        // 프리폴은 비행 타입에게 데미지를 주지 않는다(6세대).
        if (effectiveness == 0 || (move.Id == SkyDropMoveId && HasType(defender, "비행")))
            return (true, true, 0, 0, false);
        // 공중·땅속·물속에 숨은 상대를 맞히면 위력 2배 (바람일으키기·회오리, 지진·매그니튜드, 파도타기·바다회오리)
        if (DoubleOnHiddenMoveIds.Contains(move.Id) && defender.Hidden is Air or Underground or Underwater)
            power *= 2;

        // 충전: 전기 기술 위력 2배, 한 번 쓰면 끝난다.
        if (attacker.ChargeTurns > 0 && HasTypeName(move.TypeId, "전기"))
        {
            power *= 2;
            attacker.ChargeTurns = 0;
        }
        bool physical = move.Category == "물리";
        bool critical = RollCritical(attacker, defenderSide, data.MoveMetaById.GetValueOrDefault(move.Id)?.CritRate ?? 0);
        // 속임수는 상대의 공격과 공격 랭크로, 사이코쇼크 등은 상대의 방어로 계산한다. 야금야금·성스러운칼은 상대 랭크를 무시한다.
        BattlePokemon attackSource = move.Id == FoulPlayMoveId ? defender : attacker;
        bool targetsDefense = physical || DefenseTargetMoveIds.Contains(move.Id);
        int attackStage = attackSource.GetStage(physical ? Stat.Attack : Stat.SpAttack);
        int defenseStage = IgnoreStageMoveIds.Contains(move.Id) ? 0 : defender.GetStage(targetsDefense ? Stat.Defense : Stat.SpDefense);
        // 급소에 맞으면 공격 쪽의 불리한 랭크와 방어 쪽의 유리한 랭크를 무시한다.
        if (critical)
        {
            attackStage = Math.Max(0, attackStage);
            defenseStage = Math.Min(0, defenseStage);
        }
        int attack = (int)((physical ? attackSource.Attack : attackSource.SpAttack) * StageMultiplier(attackStage));
        // 원더룸: 방어와 특수방어 실제 능력치가 바뀐다 (랭크는 그대로). 모래바람: 바위 타입 특수방어 1.5배
        bool useDefense = targetsDefense != WonderRoomTurns > 0;
        double defenseStat = useDefense ? defender.Defense : defender.SpDefense;
        if (!useDefense && Weather == Sand && HasType(defender, "바위"))
            defenseStat *= 1.5;
        int defense = Math.Max(1, (int)(defenseStat * StageMultiplier(defenseStage)));

        int damage = (2 * BattlePokemon.Level / 5 + 2) * power * attack / defense / 50 + 2;
        damage = (int)(damage * FieldPowerModifier(attacker, defender, move));   // 날씨·필드·흙놀이·물놀이
        if (physical && attacker.Status == Burn && move.Id != FacadeMoveId)
            damage /= 2;                                      // 화상이면 물리 기술 데미지 절반 (객기는 그대로, 6세대)
        if (!critical && (physical ? defenderSide.ReflectTurns : defenderSide.LightScreenTurns) > 0)
            damage /= 2;                                      // 리플렉터·빛의장막 (급소면 무시)
        if (critical)
            damage = damage * 3 / 2;                          // 급소 1.5배 (6세대)
        damage = damage * Random.Next(85, 101) / 100;  // 난수 0.85~1.00
        if (attacker.Types.Contains(move.TypeId) && move.Id != StruggleMoveId)
            damage = damage * 3 / 2;                          // 자속 보정 1.5배
        damage = Math.Max(1, (int)(damage * effectiveness));  // 타입 상성

        return (true, true, damage, effectiveness, critical);
    }

    // multiHit: 연속기는 급소와 데미지를 타격마다 따로 알리므로 여기서는 상성만 적는다.
    private string DescribeMove(BattlePokemon attacker, PokemonMoveInfo move,
        (bool Supported, bool Hit, int Damage, double Effectiveness, bool Critical) result, bool multiHit = false)
    {
        string text = $"{attacker.Info.Name}의 {move.Name}!";
        if (!result.Supported) return text + " (아직 구현되지 않은 기술이라 효과가 없었다)";
        if (move.Id == MagnitudeMoveId) text += $" 매그니튜드 {attacker.MagnitudeLevel}!";
        if (!result.Hit) return text + " 공격이 빗나갔다!";
        if (result.Effectiveness == 0) return text + " 효과가 없는 것 같다...";
        if (OhkoMoveIds.Contains(move.Id)) text += " 일격필살!";
        if (result.Critical && !multiHit) text += " 급소에 맞았다!";
        if (result.Effectiveness > 1) text += " 효과가 굉장했다!";
        else if (result.Effectiveness < 1) text += " 효과가 별로인 듯하다...";
        return multiHit ? text : text + $" ({result.Damage} 데미지)";
    }

    // 마지막 글자의 받침 유무에 따라 조사를 붙인다. 예: 마기라스를, 이상해씨를, 파이리가
    public static string WithJosa(string word, string afterFinal, string afterVowel)
    {
        char last = word[^1];
        bool hasFinal = last is >= '가' and <= '힣' && (last - '가') % 28 != 0;
        return word + (hasFinal ? afterFinal : afterVowel);
    }

    // 같은 사건을 행동한 플레이어와 상대에게 각자의 시점으로 보낸다.
    public void Log(BattleSide actor, string mine, string theirs)
    {
        Send(actor, "BATTLE_LOG " + mine);
        Send(GetOther(actor), "BATTLE_LOG " + theirs);
    }

    public BattleSide GetOther(BattleSide side) =>
        side == HostSide ? GuestSide : HostSide;
}
