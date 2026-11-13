// 교체와 관련된 기술. 수치는 6세대 규칙을 따른다.
// 설치기, 교체 막기·묶기, 강제 교체, 턴 도중 교체(유턴·볼트체인지·막말내뱉기·배턴터치), 치유소원·초승달춤
partial class RoomManager
{
    private const int UTurnMoveId = 369, VoltSwitchMoveId = 521, PartingShotMoveId = 575;
    private const int RoarMoveId = 46, WhirlwindMoveId = 18, DragonTailMoveId = 525, CircleThrowMoveId = 509;
    private const int MeanLookMoveId = 212, BlockMoveId = 335, SpiderWebMoveId = 169;
    private const int SpikesMoveId = 191, ToxicSpikesMoveId = 390, StealthRockMoveId = 446, StickyWebMoveId = 564;
    private const int HealingWishMoveId = 361, LunarDanceMoveId = 461;
    // 묶기 기술: 조이기, 김밥말이, 회오리불꽃, 껍질끼우기, 바다회오리, 모래지옥, 마그마스톰, 엉겨붙기
    private static readonly HashSet<int> BindingMoveIds = [20, 35, 83, 128, 250, 328, 463, 611];

    // UseSwitchMove가 처리하는 변화 기술
    private static readonly HashSet<int> SwitchMoveIds =
    [
        RoarMoveId, WhirlwindMoveId, MeanLookMoveId, BlockMoveId, SpiderWebMoveId,
        SpikesMoveId, ToxicSpikesMoveId, StealthRockMoveId, StickyWebMoveId,
        PartingShotMoveId, HealingWishMoveId, LunarDanceMoveId
    ];
    // 상대를 노리는 변화 기술: 방어·대타에 막히고 매직코트로 되받아칠 수 있다(막말내뱉기는 소리 기술이라 대타를 무시).
    private static readonly HashSet<int> TrapMoveIds = [MeanLookMoveId, BlockMoveId, SpiderWebMoveId, PartingShotMoveId];
    // 방어·대타에는 막히지 않지만 매직코트로 되받아칠 수 있는 기술: 설치기, 울부짖기, 날려버리기
    private static readonly HashSet<int> MagicCoatOnlyMoveIds =
    [
        SpikesMoveId, ToxicSpikesMoveId, StealthRockMoveId, StickyWebMoveId, RoarMoveId, WhirlwindMoveId
    ];

    private void UseSwitchMove(GameRoom room, BattleSide attacker, BattleSide defender, PokemonMoveInfo move)
    {
        BattlePokemon user = attacker.Current, target = defender.Current;
        switch (move.Id)
        {
            case RoarMoveId:
            case WhirlwindMoveId:
                if (!ForceSwitch(room, defender))
                    LogBoth(room, attacker, "하지만 실패했다!");
                break;

            case MeanLookMoveId:
            case BlockMoveId:
            case SpiderWebMoveId:
                // 고스트 타입은 교체를 막을 수 없다(6세대).
                if (target.TrappedBy != null || HasType(target, "고스트"))
                {
                    LogBoth(room, attacker, "하지만 실패했다!");
                    break;
                }
                target.TrappedBy = user;
                LogPokemon(room, defender, $"{WithJosa(target.Info.Name, "은", "는")} 이제 도망칠 수 없다!");
                break;

            case SpikesMoveId:
                AddHazard(room, attacker, defender, defender.Spikes < 3, () => defender.Spikes++, "{0} 발밑에 압정이 흩어졌다!");
                break;
            case ToxicSpikesMoveId:
                AddHazard(room, attacker, defender, defender.ToxicSpikes < 2, () => defender.ToxicSpikes++, "{0} 발밑에 독압정이 흩어졌다!");
                break;
            case StealthRockMoveId:
                AddHazard(room, attacker, defender, !defender.StealthRock, () => defender.StealthRock = true,
                    "{0} 주변에 뾰족한 바위가 떠다니기 시작했다!");
                break;
            case StickyWebMoveId:
                AddHazard(room, attacker, defender, !defender.StickyWeb, () => defender.StickyWeb = true,
                    "{0} 발밑에 끈적끈적네트가 펼쳐졌다!");
                break;

            case PartingShotMoveId:
                // 상대 공격·특수공격 -1 뒤에 교체한다. 6세대는 능력치가 내려가지 않아도 교체한다.
                if (defender.MistTurns > 0)
                    LogPokemon(room, defender, $"{WithJosa(target.Info.Name, "은", "는")} 흰안개에 보호되고 있다!");
                else
                {
                    ChangeStage(room, defender, Stat.Attack, -1, announceFailure: true);
                    ChangeStage(room, defender, Stat.SpAttack, -1, announceFailure: true);
                }
                StartMidTurnSwitch(room, attacker, batonPass: false);
                break;

            case HealingWishMoveId:
            case LunarDanceMoveId:
                // 자신은 쓰러지고, 다음에 나오는 포켓몬의 HP와 상태이상을 모두 회복시킨다. 교체할 포켓몬이 없으면 실패한다.
                if (!HasBench(attacker))
                {
                    LogBoth(room, attacker, "하지만 실패했다!");
                    break;
                }
                attacker.HealingWish = move.Name;
                DealDamage(room, attacker, user.Hp);
                break;
        }
    }

    private static void AddHazard(GameRoom room, BattleSide attacker, BattleSide defender, bool canAdd, Action add,
        string message)
    {
        if (!canAdd)
        {
            LogBoth(room, attacker, "하지만 실패했다!");
            return;
        }
        add();
        LogSide(room, defender, message);
    }

    // 교체해서 나온 포켓몬에게 설치기·치유소원 효과를 준다.
    // 스텔스록: 최대 HP × 바위 타입 상성 / 8. 압정뿌리기: 1층 1/8, 2층 1/6, 3층 1/4.
    // 독압정: 1층 독, 2층 맹독 (독 타입이 밟으면 없어진다). 끈적끈적네트: 스피드 -1.
    // 스텔스록 말고는 땅에 있는 포켓몬에게만 효과가 있다. 치유소원·초승달춤은 설치기 다음에 HP와 상태이상을 모두 회복시킨다.
    private void ApplyEntryHazards(GameRoom room, BattleSide side)
    {
        BattlePokemon pokemon = side.Current;
        string name = pokemon.Info.Name, subject = WithJosa(name, "은", "는");
        if (side.StealthRock)
        {
            double multiplier = pokemon.Types.Aggregate(1.0, (product, type) => product * data.GetMultiplier(TypeIdOf("바위"), type));
            int damage = Math.Max(1, (int)(pokemon.MaxHp * multiplier / 8));
            Log(room, side, $"뾰족한 바위가 {name}에게 박혔다! ({damage} 데미지)", $"뾰족한 바위가 상대 {name}에게 박혔다! ({damage} 데미지)");
            DealDamage(room, side, damage);
        }
        bool grounded = IsGrounded(room, pokemon);
        if (pokemon.Hp > 0 && grounded && side.Spikes > 0)
        {
            int damage = Math.Max(1, side.Spikes switch { 1 => pokemon.MaxHp / 8, 2 => pokemon.MaxHp / 6, _ => pokemon.MaxHp / 4 });
            LogPokemon(room, side, $"{subject} 압정뿌리기의 데미지를 입었다! ({damage} 데미지)");
            DealDamage(room, side, damage);
        }
        if (pokemon.Hp > 0 && grounded && side.ToxicSpikes > 0)
        {
            if (HasType(pokemon, "독"))
            {
                side.ToxicSpikes = 0;
                LogSide(room, side, "{0} 발밑의 독압정이 사라졌다!");
            }
            else
                TryInflict(room, side, side.ToxicSpikes == 1 ? Poison : BadPoison, announceFailure: false, fromOpponent: true);
        }
        if (pokemon.Hp > 0 && grounded && side.StickyWeb)
        {
            LogPokemon(room, side, $"{subject} 끈적끈적네트에 걸렸다!");
            if (side.MistTurns > 0)
                LogPokemon(room, side, $"{subject} 흰안개에 보호되고 있다!");
            else
            {
                int[] before = pokemon.Stages.ToArray();
                ChangeStage(room, side, Stat.Speed, -1, announceFailure: true);
                SendStatEffects(room, side, pokemon, before, pokemon.FocusEnergy, TypeIdOf("벌레"));
            }
        }
        if (pokemon.Hp > 0 && side.HealingWish is string wish)
        {
            side.HealingWish = null;
            pokemon.Hp = pokemon.MaxHp;
            pokemon.Status = null;
            LogPokemon(room, side, $"{subject} {WithJosa(wish, "으로", "로")} 체력과 상태이상이 회복되었다!");
            SendHealEffect(room, side);
        }
    }

    // 고속스핀: 맞히면 자신의 묶기를 풀고 우리 편 설치기를 없앤다.
    private static void RapidSpin(GameRoom room, BattleSide side)
    {
        BattlePokemon user = side.Current;
        if (user.BoundBy != null)
        {
            LogPokemon(room, side, $"{WithJosa(user.Info.Name, "은", "는")} {user.BoundMove}에서 벗어났다!");
            user.BoundBy = null;
        }
        if (user.LeechSeeded)
        {
            LogPokemon(room, side, $"{WithJosa(user.Info.Name, "은", "는")} 씨뿌리기에서 벗어났다!");
            user.LeechSeeded = false;
        }
        if (ClearHazards(side))
            LogSide(room, side, "{0} 주변의 설치기가 사라졌다!");
    }

    // 설치기를 모두 없앤다. 있었으면 true
    private static bool ClearHazards(BattleSide side)
    {
        bool any = side.Spikes > 0 || side.ToxicSpikes > 0 || side.StealthRock || side.StickyWeb;
        side.Spikes = side.ToxicSpikes = 0;
        side.StealthRock = side.StickyWeb = false;
        return any;
    }

    // 묶기 기술에 맞은 상대는 4~5턴 동안 턴 끝에 최대 HP의 1/8 데미지를 받고 교체할 수 없다.
    private static void Bind(GameRoom room, BattleSide attacker, BattleSide defender, PokemonMoveInfo move)
    {
        BattlePokemon target = defender.Current;
        if (target.BoundBy != null)
            return;
        target.BoundBy = attacker.Current;
        target.BoundTurns = Random.Shared.Next(5, 7);   // 턴 끝마다 1씩 줄어 0이 되면 풀린다 (데미지 4~5번)
        target.BoundMove = move.Name;
        LogPokemon(room, defender, $"{WithJosa(target.Info.Name, "은", "는")} {move.Name}에 붙잡혔다!");
    }

    // 턴 끝: 묶기 데미지. 묶은 포켓몬이 교체되거나 쓰러졌으면 풀린다.
    private void TickBinding(GameRoom room, IEnumerable<BattleSide> sides)
    {
        foreach (BattleSide side in sides)
        {
            BattlePokemon pokemon = side.Current;
            if (pokemon.Hp == 0 || pokemon.BoundBy is not BattlePokemon binder)
                continue;
            string subject = WithJosa(pokemon.Info.Name, "은", "는");
            if (!ReferenceEquals(binder, GetOther(room, side).Current) || binder.Hp == 0)
            {
                pokemon.BoundBy = null;
                continue;
            }
            if (--pokemon.BoundTurns == 0)
            {
                LogPokemon(room, side, $"{subject} {pokemon.BoundMove}에서 풀려났다!");
                pokemon.BoundBy = null;
                continue;
            }
            int damage = Math.Max(1, pokemon.MaxHp / 8);
            LogPokemon(room, side, $"{subject} {pokemon.BoundMove}의 데미지를 입었다! ({damage} 데미지)");
            DealDamage(room, side, damage);
            SendBattleState(room);
        }
    }

    // 직접 교체할 수 없는지: 검은눈빛 등이나 묶기 기술을 쓴 상대가 나와 있는 동안. 고스트 타입은 교체할 수 있다(6세대).
    private bool IsTrapped(BattleSide side, BattleSide other)
    {
        BattlePokemon pokemon = side.Current, foe = other.Current;
        return !HasType(pokemon, "고스트") && foe.Hp > 0 &&
               (ReferenceEquals(pokemon.TrappedBy, foe) || ReferenceEquals(pokemon.BoundBy, foe));
    }

    private static bool HasBench(BattleSide side) =>
        side.Team.Where((pokemon, index) => index != side.Active && pokemon.Hp > 0).Any();

    // 울부짖기·날려버리기·드래곤테일·배대뒤치기: 상대의 남은 포켓몬 중 무작위로 끌어낸다.
    // 남은 포켓몬이 없거나 뿌리박기 중이면 실패한다. 끌려 나온 포켓몬은 이번 턴에 행동하지 않는다.
    private bool ForceSwitch(GameRoom room, BattleSide side)
    {
        int[] bench = Enumerable.Range(0, side.Team.Length)
            .Where(index => index != side.Active && side.Team[index].Hp > 0).ToArray();
        if (bench.Length == 0 || side.Current.Ingrained)
            return false;
        ChangeActive(room, side, bench[Random.Shared.Next(bench.Length)]);
        LogPokemon(room, side, $"{WithJosa(side.Current.Info.Name, "이", "가")} 전투에 끌려 나왔다!");
        ApplyEntryHazards(room, side);
        return true;
    }

    // 턴 도중 교체를 시작한다: 교체할 포켓몬이 있고 상대가 아직 남아 있으면 다음 포켓몬을 고를 때까지 턴 처리를 멈춘다(ContinueTurn).
    private static bool StartMidTurnSwitch(GameRoom room, BattleSide side, bool batonPass)
    {
        if (!HasBench(side) || !GetOther(room, side).Team.Any(pokemon => pokemon.Hp > 0))
            return false;
        side.SwitchingMidTurn = true;
        side.BatonPassing = batonPass;
        return true;
    }

    // 턴 도중 교체를 끝내고 남은 턴을 이어서 처리한다. 배턴터치는 랭크·기충전·혼란·아쿠아링·뿌리박기·대타를 넘겨준다.
    private void CompleteMidTurnSwitch(GameRoom room, BattleSide side, int index)
    {
        BattlePokemon previous = side.Current;
        bool batonPass = side.BatonPassing;
        side.SwitchingMidTurn = side.BatonPassing = false;
        int[] stages = previous.Stages.ToArray();
        bool focusEnergy = previous.FocusEnergy, aquaRing = previous.AquaRing, ingrained = previous.Ingrained;
        int confusion = previous.ConfusionTurns, substitute = previous.SubstituteHp;

        // 배턴터치로 넘겨주는 효과 (6세대): 씨뿌리기·저주·멸망 카운트·록온·전자부유·텔레키네시스·회복봉인·파워트릭
        var passed = (previous.LeechSeeded, previous.Cursed, previous.PerishCount, previous.LockOnTarget, previous.LockOnTurns,
            previous.MagnetRiseTurns, previous.TelekinesisTurns, previous.HealBlockTurns, previous.PowerTricked);

        ChangeActive(room, side, index);
        BattlePokemon next = side.Current;
        if (batonPass)
        {
            stages.CopyTo(next.Stages, 0);
            next.FocusEnergy = focusEnergy;
            next.ConfusionTurns = confusion;
            next.AquaRing = aquaRing;
            next.Ingrained = ingrained;
            next.SubstituteHp = substitute;
            (next.LeechSeeded, next.Cursed, next.PerishCount, next.LockOnTarget, next.LockOnTurns,
                next.MagnetRiseTurns, next.TelekinesisTurns, next.HealBlockTurns, bool powerTrick) = passed;
            if (powerTrick)
            {
                (next.Attack, next.Defense) = (next.Defense, next.Attack);
                next.PowerTricked = true;
            }
        }
        Log(room, side, $"돌아와, {previous.Info.Name}! 가라, {next.Info.Name}!",
            $"상대가 {WithJosa(previous.Info.Name, "을", "를")} 거두고 {WithJosa(next.Info.Name, "을", "를")} 내보냈다!");
        ApplyEntryHazards(room, side);
        SendBattleState(room);
        ContinueTurn(room);
    }

    // 쓰러진 포켓몬 대신 다음 포켓몬을 내보낸다 (턴을 쓰지 않음).
    private void SendInReplacement(GameRoom room, BattleSide side, int index)
    {
        ChangeActive(room, side, index);
        side.MustSwitch = false;
        Log(room, side, $"가라, {side.Current.Info.Name}!", $"상대가 {WithJosa(side.Current.Info.Name, "을", "를")} 내보냈다!");
        ApplyEntryHazards(room, side);
        SendBattleState(room);
    }
}
