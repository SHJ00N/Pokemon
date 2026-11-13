// 교체하면 풀리는 효과를 거는 변화 기술. 수치는 6세대 규칙을 따른다.
// 기술 제한(사슬묶기·앙코르·트집·도발·봉인·회복봉인), 턴마다 효과(씨뿌리기·악몽·저주·멸망의노래), 길동무,
// 명중(록온·마음의눈·꿰뚫어보기·냄새구별·미라클아이·텔레키네시스), 전자부유, 분진·송전·가로채기, 그 밖의 변화 기술
partial class RoomManager
{
    private const int DisableMoveId = 50, EncoreMoveId = 227, TormentMoveId = 259, TauntMoveId = 269,
        ImprisonMoveId = 286, HealBlockMoveId = 377;
    private const int LeechSeedMoveId = 73, NightmareMoveId = 171, CurseMoveId = 174, PerishSongMoveId = 195,
        DestinyBondMoveId = 194;
    private const int MindReaderMoveId = 170, LockOnMoveId = 199, ForesightMoveId = 193, OdorSleuthMoveId = 316,
        MiracleEyeMoveId = 357, TelekinesisMoveId = 477, MagnetRiseMoveId = 393;
    private const int PowderMoveId = 600, ElectrifyMoveId = 582, SnatchMoveId = 289;
    private const int SplashMoveId = 150, RefreshMoveId = 287, PowerTrickMoveId = 379, TopsyTurvyMoveId = 576,
        HealPulseMoveId = 505, VenomDrenchMoveId = 599, RototillerMoveId = 563, FlowerShieldMoveId = 579;
    private const int StruggleMoveId = 165;

    // UseEffectMove가 처리하는 변화 기술
    private static readonly HashSet<int> EffectMoveIds =
    [
        DisableMoveId, EncoreMoveId, TormentMoveId, TauntMoveId, ImprisonMoveId, HealBlockMoveId,
        LeechSeedMoveId, NightmareMoveId, CurseMoveId, PerishSongMoveId, DestinyBondMoveId,
        MindReaderMoveId, LockOnMoveId, ForesightMoveId, OdorSleuthMoveId, MiracleEyeMoveId, TelekinesisMoveId,
        MagnetRiseMoveId, PowderMoveId, ElectrifyMoveId, SnatchMoveId,
        SplashMoveId, PowerTrickMoveId, TopsyTurvyMoveId, HealPulseMoveId, RototillerMoveId, FlowerShieldMoveId
    ];
    // 앙코르로 계속 쓰게 할 수 없는 기술: 앙코르, 흉내내기, 따라하기, 스케치, 변신, 발버둥
    private static readonly HashSet<int> EncoreFailMoveIds = [EncoreMoveId, 102, 119, 166, 144, StruggleMoveId];

    private bool HasFlag(PokemonMoveInfo move, string flag) => data.MoveFlagsByMoveId[move.Id].Contains(flag);

    // 대타를 무시하는 기술: 소리 기술과 DB 플래그 authentic(사슬묶기·앙코르·도발·저주·이차원홀 등, 6세대)
    private bool BypassesSubstitute(PokemonMoveInfo move) => SoundMoveIds.Contains(move.Id) || HasFlag(move, "authentic");

    private void UseEffectMove(GameRoom room, BattleSide attacker, BattleSide defender, PokemonMoveInfo move)
    {
        BattlePokemon user = attacker.Current, target = defender.Current;
        string userSubject = WithJosa(user.Info.Name, "은", "는"), targetSubject = WithJosa(target.Info.Name, "은", "는");
        bool Fail()
        {
            LogBoth(room, attacker, "하지만 실패했다!");
            return false;
        }

        switch (move.Id)
        {
            // ── 기술 제한 ──
            case DisableMoveId:
                // 상대가 마지막으로 쓴 기술을 4턴 동안 쓸 수 없게 한다(이미 행동했으면 다음 4턴).
                if (target.LastMoveSlot is not int lastSlot || target.DisabledSlot != null)
                {
                    Fail();
                    break;
                }
                target.DisabledSlot = lastSlot;
                target.DisableTurns = target.MovedThisTurn ? 5 : 4;
                LogPokemon(room, defender, $"{target.Info.Name}의 {WithJosa(target.Moves[lastSlot - 1].Name, "은", "는")} 쓸 수 없게 되었다!");
                break;
            case EncoreMoveId:
                // 상대가 마지막으로 쓴 기술을 3턴 동안 계속 쓰게 한다. 이번 턴에 아직 행동하지 않았으면 그 기술로 바뀐다.
                if (target.LastMoveSlot is not int encoreSlot || target.EncoreTurns > 0 ||
                    EncoreFailMoveIds.Contains(target.Moves[encoreSlot - 1].Id))
                {
                    Fail();
                    break;
                }
                target.EncoreSlot = encoreSlot;
                target.EncoreTurns = 3;
                LogPokemon(room, defender, $"{targetSubject} 앙코르를 받았다!");
                break;
            case TormentMoveId:
                if (target.Tormented)
                {
                    Fail();
                    break;
                }
                target.Tormented = true;
                LogPokemon(room, defender, $"{targetSubject} 트집을 잡혔다!");
                break;
            case TauntMoveId:
                // 3턴 동안 변화 기술을 쓸 수 없다. 상대가 이미 행동했으면 다음 3턴
                if (target.TauntTurns > 0)
                {
                    Fail();
                    break;
                }
                target.TauntTurns = target.MovedThisTurn ? 4 : 3;
                LogPokemon(room, defender, $"{targetSubject} 도발에 넘어갔다!");
                break;
            case ImprisonMoveId:
                if (user.Imprisoning)
                {
                    Fail();
                    break;
                }
                user.Imprisoning = true;
                LogPokemon(room, attacker, $"{userSubject} 상대가 같은 기술을 쓰지 못하게 봉인했다!");
                break;
            case HealBlockMoveId:
                if (target.HealBlockTurns > 0)
                {
                    Fail();
                    break;
                }
                target.HealBlockTurns = 5;
                LogPokemon(room, defender, $"{targetSubject} 회복을 봉인당했다!");
                break;

            // ── 턴마다 효과 ──
            case LeechSeedMoveId:
                if (HasType(target, "풀"))
                {
                    LogBoth(room, attacker, "효과가 없는 것 같다...");
                    break;
                }
                if (target.LeechSeeded)
                {
                    Fail();
                    break;
                }
                target.LeechSeeded = true;
                LogPokemon(room, defender, $"{target.Info.Name}에게 씨를 심었다!");
                break;
            case NightmareMoveId:
                if (target.Status != Sleep || target.Nightmare)
                {
                    Fail();
                    break;
                }
                target.Nightmare = true;
                LogPokemon(room, defender, $"{targetSubject} 악몽을 꾸기 시작했다!");
                break;
            case CurseMoveId:
                // 고스트 타입: 자신의 HP 1/2을 써서 상대에게 저주(턴 끝 1/4). 아니면 스피드 -1, 공격 +1, 방어 +1
                if (!HasType(user, "고스트"))
                {
                    ChangeStage(room, attacker, Stat.Speed, -1, announceFailure: true);
                    ChangeStage(room, attacker, Stat.Attack, 1, announceFailure: true);
                    ChangeStage(room, attacker, Stat.Defense, 1, announceFailure: true);
                    break;
                }
                if (target.Cursed)
                {
                    Fail();
                    break;
                }
                target.Cursed = true;
                LogPokemon(room, attacker, $"{userSubject} 자신의 체력을 깎아서 상대에게 저주를 걸었다!");
                DealDamage(room, attacker, Math.Max(1, user.MaxHp / 2));
                break;
            case PerishSongMoveId:
                // 노래를 들은 양쪽 포켓몬은 3턴 뒤 턴 끝에 쓰러진다. 이미 카운트가 있는 포켓몬은 그대로
                BattlePokemon[] listeners = new[] { user, target }.Where(pokemon => pokemon.PerishCount == 0).ToArray();
                if (listeners.Length == 0)
                {
                    Fail();
                    break;
                }
                foreach (BattlePokemon pokemon in listeners)
                    pokemon.PerishCount = 4;    // 쓴 턴 끝에 3이 된다.
                LogBoth(room, attacker, "멸망의노래를 들은 포켓몬은 3턴 후에 쓰러진다!");
                break;
            case DestinyBondMoveId:
                user.DestinyBond = true;
                LogPokemon(room, attacker, $"{userSubject} 상대를 길동무로 삼으려 하고 있다!");
                break;

            // ── 명중·땅 ──
            case MindReaderMoveId:
            case LockOnMoveId:
                user.LockOnTarget = target;
                user.LockOnTurns = 2;   // 다음 턴이 끝날 때까지
                LogPokemon(room, attacker, $"{userSubject} 상대 {WithJosa(target.Info.Name, "을", "를")} 노렸다!");
                break;
            case ForesightMoveId:
            case OdorSleuthMoveId:
                target.Identified = true;
                LogPokemon(room, defender, $"{target.Info.Name}의 정체를 꿰뚫어 보았다!");
                break;
            case MiracleEyeMoveId:
                target.MiracleEye = true;
                LogPokemon(room, defender, $"{target.Info.Name}의 정체를 꿰뚫어 보았다!");
                break;
            case TelekinesisMoveId:
                // 3턴 동안 떠올라 기술이 반드시 맞고(일격필살 제외) 땅 기술은 맞지 않는다.
                if (target.TelekinesisTurns > 0 || target.Ingrained || target.SmackedDown || room.GravityTurns > 0)
                {
                    Fail();
                    break;
                }
                target.TelekinesisTurns = 3;
                LogPokemon(room, defender, $"{targetSubject} 공중에 떠올랐다!");
                break;
            case MagnetRiseMoveId:
                // 5턴 동안 땅 기술을 맞지 않는다.
                if (user.MagnetRiseTurns > 0 || user.Ingrained || user.SmackedDown || room.GravityTurns > 0)
                {
                    Fail();
                    break;
                }
                user.MagnetRiseTurns = 5;
                LogPokemon(room, attacker, $"{userSubject} 전자부유로 떠올랐다!");
                break;

            // ── 이번 턴 ──
            case PowderMoveId:
                target.Powdered = true;
                LogPokemon(room, defender, $"{targetSubject} 분진을 뒤집어썼다!");
                break;
            case ElectrifyMoveId:
                // 상대가 이번 턴에 아직 행동하지 않았으면 그 기술이 전기 타입이 된다.
                if (target.MovedThisTurn)
                {
                    Fail();
                    break;
                }
                target.Electrified = true;
                LogPokemon(room, defender, $"{target.Info.Name}의 기술이 전기 타입이 되었다!");
                break;
            case SnatchMoveId:
                user.Snatching = true;
                LogPokemon(room, attacker, $"{userSubject} 상대의 기술을 가로챌 준비를 했다!");
                break;

            // ── 그 밖의 변화 기술 ──
            case SplashMoveId:
                LogBoth(room, attacker, "하지만 아무 일도 일어나지 않았다!");
                break;
            case PowerTrickMoveId:
                (user.Attack, user.Defense) = (user.Defense, user.Attack);
                user.PowerTricked = !user.PowerTricked;
                LogPokemon(room, attacker, $"{userSubject} 공격과 방어를 바꿨다!");
                break;
            case TopsyTurvyMoveId:
                if (target.Stages.All(stage => stage == 0))
                {
                    Fail();
                    break;
                }
                for (int index = 0; index < target.Stages.Length; index++)
                    target.Stages[index] = -target.Stages[index];
                LogPokemon(room, defender, $"{target.Info.Name}의 능력 변화가 뒤집혔다!");
                break;
            case HealPulseMoveId:
                // 상대의 HP를 최대 HP의 1/2 회복시킨다.
                if (!Heal(room, defender, target.MaxHp / 2))
                    Fail();
                break;
            case RototillerMoveId:
            case FlowerShieldMoveId:
                // 일구기: 땅에 있는 풀 타입 공격·특수공격 +1, 플라워가드: 풀 타입 방어 +1 (양쪽 모두)
                BattleSide[] grass = new[] { attacker, defender }.Where(side => HasType(side.Current, "풀") &&
                    (move.Id == FlowerShieldMoveId || IsGrounded(room, side.Current))).ToArray();
                if (grass.Length == 0)
                {
                    Fail();
                    break;
                }
                foreach (BattleSide side in grass)
                {
                    BattlePokemon pokemon = side.Current;
                    int[] before = pokemon.Stages.ToArray();
                    if (move.Id == RototillerMoveId)
                    {
                        ChangeStage(room, side, Stat.Attack, 1, announceFailure: true);
                        ChangeStage(room, side, Stat.SpAttack, 1, announceFailure: true);
                    }
                    else
                        ChangeStage(room, side, Stat.Defense, 1, announceFailure: true);
                    if (side != attacker)
                        SendStatEffects(room, side, pokemon, before, pokemon.FocusEnergy, move.TypeId);
                }
                break;
        }
    }

    // 지금 그 슬롯의 기술을 쓸 수 없는 이유(사슬묶기·앙코르·트집·도발·회복봉인·봉인). 쓸 수 있으면 null
    private string? MoveBlockedBy(BattleSide side, BattleSide other, int slot)
    {
        BattlePokemon pokemon = side.Current;
        PokemonMoveInfo move = pokemon.Moves[slot - 1];
        if (pokemon.DisabledSlot == slot) return "사슬묶기";
        if (pokemon.EncoreTurns > 0 && pokemon.EncoreSlot != slot) return "앙코르";
        if (pokemon.Tormented && pokemon.LastMoveSlot is int last && pokemon.Moves[last - 1].Id == move.Id) return "트집";
        if (pokemon.TauntTurns > 0 && move.Category == "변화") return "도발";
        if (pokemon.HealBlockTurns > 0 && HasFlag(move, "heal")) return "회복봉인";
        if (other.Current.Imprisoning && other.Current.Hp > 0 && other.Current.Moves.Any(known => known.Id == move.Id)) return "봉인";
        return null;
    }

    // 쓸 수 있는 기술이 하나도 없으면 발버둥을 쓴다.
    private bool MustStruggle(BattleSide side, BattleSide other) =>
        Enumerable.Range(1, side.Current.Moves.Length).All(slot => MoveBlockedBy(side, other, slot) != null);

    // 턴 끝: 씨뿌리기 (화상·독 데미지보다 먼저). 씨를 심은 쪽 자리의 포켓몬이 빼앗은 만큼 회복한다.
    private void TickLeechSeed(GameRoom room, IEnumerable<BattleSide> sides)
    {
        foreach (BattleSide side in sides)
        {
            BattlePokemon pokemon = side.Current;
            if (pokemon.Hp == 0 || !pokemon.LeechSeeded)
                continue;
            int damage = Math.Min(pokemon.Hp, Math.Max(1, pokemon.MaxHp / 8));
            Log(room, side, $"씨뿌리기가 {pokemon.Info.Name}의 체력을 빼앗는다! ({damage} 데미지)",
                $"씨뿌리기가 상대 {pokemon.Info.Name}의 체력을 빼앗는다! ({damage} 데미지)");
            DealDamage(room, side, damage);
            Heal(room, GetOther(room, side), damage);
            SendBattleState(room);
        }
    }

    // 턴 끝: 악몽·저주 데미지 → 멸망의노래 카운트 → 기술 제한·록온·텔레키네시스·전자부유의 남은 턴
    private void TickEffects(GameRoom room, IEnumerable<BattleSide> sides)
    {
        foreach (BattleSide side in sides)
        {
            BattlePokemon pokemon = side.Current;
            string subject = WithJosa(pokemon.Info.Name, "은", "는");
            if (pokemon.Nightmare && pokemon.Status != Sleep)
                pokemon.Nightmare = false;
            if (pokemon.Hp > 0 && pokemon.Nightmare)
            {
                int damage = Math.Max(1, pokemon.MaxHp / 4);
                LogPokemon(room, side, $"{subject} 악몽에 시달리고 있다! ({damage} 데미지)");
                DealDamage(room, side, damage);
            }
            if (pokemon.Hp > 0 && pokemon.Cursed)
            {
                int damage = Math.Max(1, pokemon.MaxHp / 4);
                LogPokemon(room, side, $"{subject} 저주를 받고 있다! ({damage} 데미지)");
                DealDamage(room, side, damage);
            }
        }
        foreach (BattleSide side in sides)
        {
            BattlePokemon pokemon = side.Current;
            if (pokemon.Hp == 0 || pokemon.PerishCount == 0)
                continue;
            LogPokemon(room, side, $"{pokemon.Info.Name}의 멸망 카운트가 {--pokemon.PerishCount}{(pokemon.PerishCount == 2 ? "가" : "이")} 되었다!");
            if (pokemon.PerishCount == 0)
                DealDamage(room, side, pokemon.Hp);
        }
        foreach (BattleSide side in sides)
        {
            BattlePokemon pokemon = side.Current;
            string name = pokemon.Info.Name;
            if (pokemon.DisableTurns > 0 && --pokemon.DisableTurns == 0)
            {
                pokemon.DisabledSlot = null;
                LogPokemon(room, side, $"{name}의 사슬묶기가 풀렸다!");
            }
            if (pokemon.EncoreTurns > 0 && --pokemon.EncoreTurns == 0)
            {
                pokemon.EncoreSlot = null;
                LogPokemon(room, side, $"{name}의 앙코르 효과가 풀렸다!");
            }
            if (pokemon.TauntTurns > 0 && --pokemon.TauntTurns == 0)
                LogPokemon(room, side, $"{name}의 도발 효과가 풀렸다!");
            if (pokemon.HealBlockTurns > 0 && --pokemon.HealBlockTurns == 0)
                LogPokemon(room, side, $"{name}의 회복봉인이 풀렸다!");
            if (pokemon.TelekinesisTurns > 0 && --pokemon.TelekinesisTurns == 0)
                LogPokemon(room, side, $"{name}의 텔레키네시스 효과가 풀렸다!");
            if (pokemon.MagnetRiseTurns > 0 && --pokemon.MagnetRiseTurns == 0)
                LogPokemon(room, side, $"{name}의 전자부유 효과가 풀렸다!");
            if (pokemon.LockOnTurns > 0 && --pokemon.LockOnTurns == 0)
                pokemon.LockOnTarget = null;
        }
        SendBattleState(room);
    }
}
