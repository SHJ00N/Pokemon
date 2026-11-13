// 기술 실행과 상태이상 처리. 수치는 6세대 규칙을 따른다.
// 상태이상 종류와 기술별 확률·대상·시점은 DB의 StatusCondition, MoveStatusEffect에서 읽는다.
partial class RoomManager
{
    // StatusCondition.code
    private const string Paralysis = "paralysis", Sleep = "sleep", Freeze = "freeze", Burn = "burn",
        Poison = "poison", BadPoison = "bad-poison", Confusion = "confusion", Infatuation = "infatuation";

    private const int RestMoveId = 156;         // 잠자기: HP를 모두 회복하고 3턴 잠든다.
    private const int ThunderWaveMoveId = 86;   // 전기자석파: 땅 타입에게 효과가 없다.
    private const int ToxicMoveId = 92;         // 맹독: 독 타입이 쓰면 반드시 맞는다(6세대).
    private const int ScaldMoveId = 503, SteamEruptionMoveId = 592;   // 열탕·스팀버스트: 맞은 상대의 얼음을 녹인다(6세대).
    // 얼어 있어도 쓰면 스스로 얼음이 녹는 기술: 화염바퀴, 성스러운불꽃, 플레어드라이브, 열탕, 크로스플레임, 스팀버스트
    private static readonly HashSet<int> SelfThawMoveIds = [172, 221, 394, 503, 558, 592];
    // 가루·포자 기술: 6세대부터 풀 타입에게 효과가 없다.
    // 독가루, 저리가루, 수면가루, 버섯포자, 목화포자, 분노가루, 분진
    private static readonly HashSet<int> PowderMoveIds = [77, 78, 79, 147, 178, 476, 600];

    // 조건이 있는 공격기: 속이기, 코골기, 꿈먹기, 트리플킥
    private const int FakeOutMoveId = 252, SnoreMoveId = 173, DreamEaterMoveId = 138, TripleKickMoveId = 167;
    // DB(최신 세대)와 6세대의 능력치 변화가 다른 공격기: 고속스핀(스피드 +1은 8세대부터), 다이아스톰(6세대는 방어 +1)
    private const int RapidSpinMoveId = 229, DiamondStormMoveId = 591;
    // 2~5번 맞는 연속기의 타격 횟수: 2번·3번 각 1/3, 4번·5번 각 1/6 (5세대부터)
    private static readonly int[] TwoToFiveHits = [2, 2, 3, 3, 4, 5];

    // 상태이상에 따라 위력이 2배가 되는 공격기: 객기(자신이 화상·독·마비), 병상첨병(상대가 상태이상), 베놈쇼크(상대가 독)
    private const int FacadeMoveId = 263, HexMoveId = 506, VenoshockMoveId = 474;

    // 상태이상을 고치거나 옮기는 변화 기술: 치료방울, 아로마테라피, 사이코시프트
    private const int HealBellMoveId = 215, AromatherapyMoveId = 312, PsychoShiftMoveId = 375;
    private static readonly HashSet<int> StatusCureMoveIds = [HealBellMoveId, AromatherapyMoveId, PsychoShiftMoveId, RefreshMoveId];

    // 특성·지닌 도구가 없고 싱글배틀만 하는 이 게임에서는 효과가 없어서 항상 실패하는 기술
    // 특성: 역할, 스킬스왑, 위액, 고민씨, 심플빔, 동료만들기
    // 도구: 트릭, 바꿔치기, 리사이클, 금제, 기프트패스, 자연의은혜
    // 더블배틀 전용: 날따름, 분노가루, 도우미, 사이드체인지, 아로마미스트, 자기장조작, 당신먼저, 순서미루기
    private static readonly HashSet<int> FailingMoveIds =
    [
        272, 285, 380, 388, 493, 494,
        271, 415, 278, 373, 516, 363,
        266, 476, 270, 502, 597, 602, 495, 511,
        478,    // 매직룸: 지닌 도구의 효과를 없애는 공간
        562,    // 트림: 나무열매를 먹은 뒤에만 쓸 수 있다.
        100,    // 순간이동: 트레이너 배틀에서는 실패한다.
        180, 288    // 원한·원념: PP를 깎는 기술인데 이 게임에는 PP가 없다.
    ];

    // 잠꼬대: 잠든 동안 자신의 다른 기술 하나를 무작위로 쓴다.
    private const int SleepTalkMoveId = 214;
    // 잠꼬대로 나오지 않는 기술 (6세대): 잠꼬대, 소란피기, 힘껏펀치, 참기, 수다, 트림, 손잡기, 다른 기술을 부르는 기술
    // (조수·흉내쟁이·선취·손가락흔들기·흉내내기·따라하기·자연의힘·스케치), 2턴 기술(칼바람·공중날기·솔라빔·구멍파기·
    // 로켓박치기·불새·다이빙·뛰어오르기·섀도다이브·프리폴·프리즈볼트·콜드플레어·고스트다이브·지오컨트롤)
    private static readonly HashSet<int> SleepTalkBannedMoveIds =
    [
        214, 253, 264, 117, 448, 562, 607, 274, 383, 382, 118, 102, 119, 267, 166,
        13, 19, 76, 91, 130, 143, 291, 340, 467, 507, 553, 554, 566, 601
    ];

    // 한 포켓몬의 기술 한 번을 처리한다. 반동·붙잡힘 → 행동 가능 판정 → 2턴 기술 첫 턴 → 숨은 상대·방어·매직코트
    // → 기술 효과 → 자폭·반동 → 난동 기술 종료 순서다.
    // movesLast: 이번 턴에 이 포켓몬 뒤로 행동할 포켓몬이 없는지 (방어·매직코트는 이때 실패한다)
    private void ExecuteMove(GameRoom room, BattleSide attacker, BattleSide defender, bool movesLast)
    {
        BattlePokemon user = attacker.Current;
        // 이번 턴에 먼저 쓰러졌거나, 상대가 이미 쓰러졌으면 기술을 쓰지 않는다.
        if (user.Hp == 0 || defender.Current.Hp == 0)
            return;
        user.MovedThisTurn = true;
        user.DestinyBond = false;   // 길동무는 다음 행동 전까지만

        int slot = attacker.ChosenMove!.Value;
        // 앙코르: 고른 기술 대신 앙코르 받은 기술을 쓴다. 쓸 수 있는 기술이 없으면 발버둥
        if (user.EncoreTurns > 0 && user.EncoreSlot is int encoreSlot && !attacker.Struggling)
            slot = encoreSlot;
        PokemonMoveInfo move = attacker.Struggling ? data.MovesById[StruggleMoveId] : user.Moves[slot - 1];
        string subject = WithJosa(user.Info.Name, "은", "는");
        // 파괴광선 등을 맞힌 다음 턴은 반동으로 움직이지 못한다. 프리폴에 붙잡힌 포켓몬도 움직이지 못한다.
        if (user.RechargeSlot != null)
        {
            user.RechargeSlot = null;
            LogPokemon(room, attacker, $"{subject} 공격의 반동으로 움직일 수 없다!");
            return;
        }
        if (user.SkyDropped)
        {
            LogPokemon(room, attacker, $"{subject} 공중에 붙잡혀 있어서 움직일 수 없다!");
            return;
        }

        user.MoveActions++;
        if (!CanAct(room, attacker, move))
        {
            // 난동 중에 행동하지 못하면 난동이 혼란 없이 끝난다. 2턴 기술·구르기·참기는 취소된다.
            user.LockedSlot = null;
            user.LockTurnsLeft = user.RolloutHits = user.BideTurns = user.UproarTurns = 0;
            EndCharging(attacker, defender);
            return;
        }
        // 2턴 기술의 공격 턴: 모으던 기술을 그대로 쓴다 (손가락흔들기 등으로 나온 기술도)
        bool continuing = user.ChargingSlot != null && user.ChargingMove != null;
        if (continuing)
            move = user.ChargingMove!;
        // 사슬묶기·도발·트집·회복봉인·봉인: 기술을 고른 뒤에 걸렸으면 그 기술을 쓰지 못한다.
        if (!attacker.Struggling && !continuing && user.LockedSlot == null &&
            MoveBlockedBy(attacker, defender, slot) is string cause)
        {
            LogPokemon(room, attacker, $"{subject} {cause} 때문에 {WithJosa(move.Name, "을", "를")} 쓸 수 없다!");
            return;
        }
        // 힘껏펀치: 기술을 쓰기 전에 이번 턴에 공격을 맞았으면 실패한다.
        if (move.Id == FocusPunchMoveId && user.HitThisTurn)
        {
            LogPokemon(room, attacker, $"{subject} 기합이 흐트러져서 기술을 쓸 수 없었다!");
            return;
        }
        user.UsedMoveIds.Add(move.Id);
        user.LastMoveSlot = attacker.Struggling ? null : slot;
        if (move.Id != RageMoveId)
            user.Raging = false;    // 분노는 다른 기술을 쓰면 끝난다.

        // 잠꼬대·손가락흔들기·따라하기 등: 다른 기술을 골라 그 기술로 처리한다. 고를 기술이 없으면 실패
        bool sleepTalk = !continuing && (move.Id == SleepTalkMoveId || CallingMoveIds.Contains(move.Id));
        if (sleepTalk)
        {
            PokemonMoveInfo? called = move.Id == SleepTalkMoveId
                ? PickSleepTalkMove(room, attacker, move)
                : PickCalledMove(room, attacker, defender, move);
            if (called == null)
                return;
            move = called;
        }
        user.LastExecutedMove = room.LastExecutedMove = move;
        // 분진: 불꽃 기술을 쓰려 하면 폭발해서 최대 HP의 1/4 데미지를 받고 기술은 실패한다.
        if (user.Powdered && HasTypeName(AdjustMove(room, user, move).TypeId, "불꽃"))
        {
            int damage = Math.Max(1, user.MaxHp / 4);
            LogPokemon(room, attacker, $"{user.Info.Name}의 {move.Name}!");
            LogBoth(room, attacker, $"분진이 폭발했다! ({damage} 데미지)");
            DealDamage(room, attacker, damage);
            return;
        }
        // 중력 중에는 공중에 뜨거나 뛰어오르는 기술을 쓸 수 없다.
        if (room.GravityTurns > 0 && GravityBannedMoveIds.Contains(move.Id))
        {
            LogPokemon(room, attacker, $"{subject} 중력 때문에 {WithJosa(move.Name, "을", "를")} 쓸 수 없다!");
            return;
        }
        // 참기: 참는 턴이면 여기서 끝나고, 돌려줄 차례면 아래에서 공격으로 처리한다.
        if (move.Id == BideMoveId && !UseBide(room, attacker, move, slot))
            return;
        // 에코보이스: 매 턴 이어서 쓰이면(어느 쪽이든) 위력이 40씩 오른다(최대 200). 한 턴이라도 쉬면 처음부터
        if (move.Id == EchoedVoiceMoveId && room.EchoedVoiceTurn != room.TurnNumber)
        {
            room.EchoedVoiceCount = room.EchoedVoiceTurn == room.TurnNumber - 1 ? Math.Min(5, room.EchoedVoiceCount + 1) : 1;
            room.EchoedVoiceTurn = room.TurnNumber;
        }

        // 난동 기술(DB의 after_sequence 효과가 있는 기술)은 2~3턴 동안 같은 기술만 쓴다. 잠꼬대로 나오면 고정하지 않는다.
        bool rampage = !sleepTalk && data.MoveStatusEffectsByMoveId[move.Id].Any(e => e.Timing == "after_sequence");
        if (rampage && user.LockedSlot == null)
        {
            user.LockedSlot = slot;
            user.LockTurnsLeft = Random.Shared.Next(2, 4);
        }

        // 가로채기: 상대가 이번 턴에 가로채기를 썼으면 자신을 대상으로 하는 변화 기술(DB 플래그 snatch)을 상대가 대신 쓴다.
        BattlePokemon snatcher = defender.Current;
        if (HasFlag(move, "snatch") && snatcher.Snatching && snatcher.Hp > 0)
        {
            snatcher.Snatching = false;
            LogPokemon(room, attacker, $"{user.Info.Name}의 {move.Name}!");
            LogPokemon(room, defender, $"{WithJosa(snatcher.Info.Name, "은", "는")} 상대의 {WithJosa(move.Name, "을", "를")} 가로챘다!");
            UseStatusMove(room, defender, attacker, move, movesLast: false, reflected: true);
            return;
        }

        // 2턴 기술: 첫 턴에는 모으기만 한다. 프리폴은 첫 턴에 상대를 붙잡으므로 아래에서 명중·방어를 확인한다.
        // 쾌청일 때 솔라빔은 모으지 않고 바로 쏜다.
        bool chargeTurn = TwoTurnMoves.ContainsKey(move.Id) && !continuing &&
                          !(move.Id == SolarBeamMoveId && room.Weather == Sun);
        if (chargeTurn && move.Id != SkyDropMoveId)
        {
            StartCharging(room, attacker, move, slot);
            return;
        }
        if (!chargeTurn)
            EndCharging(attacker, defender);

        BattlePokemon target = defender.Current;
        bool landed = false;    // 공격이 맞았는지 (대타 포함). 반동으로 쉬기·빗나감 반동에 쓴다.
        if (move.Id is FutureSightMoveId or DoomDesireMoveId)
            UseFutureAttack(room, attacker, defender, move);
        else if (FailingMoveIds.Contains(move.Id))
        {
            LogPokemon(room, attacker, $"{user.Info.Name}의 {move.Name}!");
            LogBoth(room, attacker, "하지만 실패했다!");
        }
        else if (TargetsOpponent(move) && target.Hidden is string hidden && !HitsHiddenTarget(user, move, hidden) &&
                 !ReferenceEquals(user.LockOnTarget, target))
            LogMissHidden(room, attacker, move);
        else if (TargetsOpponent(move) && IsProtectedFrom(defender, move) && !BreaksProtectMoveIds.Contains(move.Id))
        {
            LogPokemon(room, attacker, $"{user.Info.Name}의 {move.Name}!");
            LogPokemon(room, defender, $"{WithJosa(target.Info.Name, "은", "는")} 공격으로부터 몸을 지켰다!");
            ProtectContactEffect(room, attacker, defender, move);   // 킹실드·니들가드
        }
        else if (target.MagicCoat && IsReflectable(move))
        {
            // 매직코트: 상대를 노린 변화 기술을 쓴 쪽에게 되돌려 보낸다.
            LogPokemon(room, attacker, $"{user.Info.Name}의 {move.Name}!");
            LogPokemon(room, defender, $"{WithJosa(target.Info.Name, "은", "는")} {WithJosa(move.Name, "을", "를")} 되받아쳤다!");
            UseStatusMove(room, defender, attacker, move, movesLast: false, reflected: true);
        }
        else if (chargeTurn)
            StartSkyDrop(room, attacker, defender, move, slot);
        else if (move.Category == "변화")
            UseStatusMove(room, attacker, defender, move, movesLast, slot: slot);
        else
        {
            BreakProtection(room, defender, move);
            landed = UseDamagingMove(room, attacker, defender, move);
        }

        // 발버둥: 최대 HP의 1/4만큼 반동을 받는다.
        if (attacker.Struggling && user.Hp > 0)
        {
            int recoil = Math.Max(1, user.MaxHp / 4);
            LogPokemon(room, attacker, $"{subject} 반동으로 데미지를 입었다! ({recoil} 데미지)");
            DealDamage(room, attacker, recoil);
        }
        if (move.Id == UproarMoveId)
            Uproar(room, attacker, landed, sleepTalk, slot);
        if (move.Id == RageMoveId && user.Hp > 0)
            user.Raging = true;

        if (CrashMoveIds.Contains(move.Id) && !landed && user.Hp > 0)
            Crash(room, attacker);
        if (RechargeMoveIds.Contains(move.Id) && landed && user.Hp > 0)
            user.RechargeSlot = slot;
        if (move.Id == RapidSpinMoveId && landed && user.Hp > 0)
            RapidSpin(room, attacker);
        // 유턴·볼트체인지: 맞히면(대타 포함, 상대가 쓰러져도) 다음 포켓몬을 골라 교체한다.
        if (move.Id is UTurnMoveId or VoltSwitchMoveId && landed && user.Hp > 0)
            StartMidTurnSwitch(room, attacker, batonPass: false);
        // 자폭·대폭발은 빗나가거나 막혀도 쓴 포켓몬이 쓰러진다. 목숨걸기는 맞혔을 때 쓰러진다.
        if (move.Id is SelfDestructMoveId or ExplosionMoveId && user.Hp > 0)
            DealDamage(room, attacker, user.Hp);
        if (move.Id == FinalGambitMoveId && landed && user.Hp > 0)
            DealDamage(room, attacker, user.Hp);
        // 구르기·아이스볼: 맞히면 5번까지 같은 기술을 이어서 쓴다(LOCKED). 빗나가거나 5번째면 끝난다.
        if (move.Id is RolloutMoveId or IceBallMoveId)
        {
            user.RolloutHits = landed && user.RolloutHits < 4 ? user.RolloutHits + 1 : 0;
            user.LockedSlot = user.RolloutHits > 0 && user.Hp > 0 ? slot : null;
        }
        user.FuryCutterHits = move.Id == FuryCutterMoveId && landed ? user.FuryCutterHits + 1 : 0;
        if (move.Id == BideMoveId)
            user.BideDamage = 0;
        // 토해내기: 쓰고 나면(빗나가도) 비축과 비축으로 오른 방어·특수방어가 원래대로 돌아간다.
        if (move.Id == SpitUpMoveId && user.StockpileCount > 0)
        {
            ChangeStage(room, attacker, Stat.Defense, -user.StockpileDefense, announceFailure: false);
            ChangeStage(room, attacker, Stat.SpDefense, -user.StockpileSpDefense, announceFailure: false);
            user.StockpileCount = user.StockpileDefense = user.StockpileSpDefense = 0;
        }
        room.LastMoveIdThisTurn = landed || move.Category == "변화" ? move.Id : 0;

        if (rampage && user.LockedSlot != null && --user.LockTurnsLeft == 0)
        {
            user.LockedSlot = null;
            if (user.Hp > 0)
                ApplyStatusEffects(room, attacker, defender, move, "after_sequence", announceFailure: false);
        }
    }

    // 잠꼬대로 쓸 기술을 고른다. 깨어 있거나 고를 수 있는 기술이 없으면 실패하고 null을 돌려준다.
    private static PokemonMoveInfo? PickSleepTalkMove(GameRoom room, BattleSide side, PokemonMoveInfo sleepTalk)
    {
        BattlePokemon user = side.Current;
        LogPokemon(room, side, $"{user.Info.Name}의 {sleepTalk.Name}!");
        PokemonMoveInfo[] candidates = user.Moves.Where(move => !SleepTalkBannedMoveIds.Contains(move.Id)).ToArray();
        if (user.Status != Sleep || candidates.Length == 0)
        {
            LogBoth(room, side, "하지만 실패했다!");
            return null;
        }
        return candidates[Random.Shared.Next(candidates.Length)];
    }

    // 공격이 맞았으면(대타가 맞은 것 포함) true
    private bool UseDamagingMove(GameRoom room, BattleSide attacker, BattleSide defender, PokemonMoveInfo move)
    {
        BattlePokemon user = attacker.Current, target = defender.Current;
        // 속이기는 나온 뒤 첫 행동에서만, 코골기는 자신이 잠들어 있을 때만, 꿈먹기는 상대가 잠들어 있을 때만,
        // 기습은 상대가 아직 행동하지 않았고 공격 기술을 골랐을 때만, 비장의무기는 다른 기술을 모두 쓴 뒤에만 성공한다.
        if ((move.Id == FakeOutMoveId && user.MoveActions > 1) || (move.Id == SnoreMoveId && user.Status != Sleep) ||
            (move.Id == DreamEaterMoveId && target.Status != Sleep) ||
            (move.Id == SuckerPunchMoveId && !WillAttack(defender)) ||
            (move.Id == LastResortMoveId && !CanUseLastResort(user)) ||
            PowerMoveFails(attacker, user, target, move))
        {
            LogPokemon(room, attacker, $"{user.Info.Name}의 {move.Name}!");
            LogBoth(room, attacker, "하지만 실패했다!");
            return false;
        }
        if (move.Id == MagnitudeMoveId)
            RollMagnitude(user);
        if (move.Id == PresentMoveId && !RollPresent(room, attacker, defender, move))
            return false;

        move = AdjustMove(room, user, move);  // 웨더볼·솔라빔·번개·잠재파워·송전 등 타입·위력·명중률
        BreakScreens(room, defender, move);
        var result = CalculateDamage(room, attacker, defender, move);
        bool hit = result.Supported && result.Hit && result.Effectiveness > 0;
        SendMoveEffect(room, attacker, move, hit ? result.Effectiveness : 0);
        // 집단구타는 참여하는 포켓몬 수만큼 때린다.
        int hits = !hit ? 1 : move.Id == BeatUpMoveId ? BeatUpMembers(attacker).Length : RollHitCount(move);
        LogPokemon(room, attacker, DescribeMove(user, move, result, multiHit: hits > 1));
        if (!hit)
            return false;

        // 한 번씩 맞힌다 (ApplyHit: 대타·버티기 처리).
        int dealt = 0, bodyHits = 0;    // 대타를 포함해 실제로 준 데미지(흡수·반동 기준), 포켓몬 본체에 맞은 횟수
        bool endured = false;
        List<string> damages = [];
        for (int count = 1; count <= hits; count++)
        {
            if (count > 1)
            {
                result = CalculateDamage(room, attacker, defender, move, count);
                if (!result.Hit)
                    break;  // 트리플킥은 빗나가면 거기서 끝난다.
            }
            damages.Add((result.Critical ? "급소 " : "") + result.Damage);
            var (hitDealt, body, hitEndured) = ApplyHit(room, defender, move, result.Damage);
            dealt += hitDealt;
            endured |= hitEndured;
            if (!body)
                continue;
            bodyHits++;
            if (target.Hp == 0)
                break;
        }
        if (hits > 1)
            LogBoth(room, attacker, $"{damages.Count}번 맞았다! ({string.Join(" + ", damages)} 데미지)");
        if (endured)
            LogPokemon(room, defender, $"{WithJosa(target.Info.Name, "은", "는")} 공격을 버텼다!");
        if (target.Hp == 0)
            Faint(room, defender);
        // 길동무: 길동무를 쓴 포켓몬을 쓰러뜨리면 공격한 포켓몬도 쓰러진다.
        if (target.Hp == 0 && target.DestinyBond && user.Hp > 0)
        {
            LogPokemon(room, defender, $"{WithJosa(target.Info.Name, "은", "는")} 상대를 길동무로 삼았다!");
            DealDamage(room, attacker, user.Hp);
        }

        int[] userStages = user.Stages.ToArray(), targetStages = target.Stages.ToArray();
        // 상대에게 거는 추가 효과는 본체에 맞은 횟수만큼 판정한다. (대타가 맞았거나 상대가 쓰러졌으면 없다)
        for (int count = 0; count < bodyHits && target.Hp > 0; count++)
            ApplyTargetEffects(room, attacker, defender, move);
        // 자신에게 오는 효과는 상대가 쓰러졌거나 대타가 맞았어도 적용된다.
        ApplyUserEffects(room, attacker, move, dealt);
        // 마지막일침: 상대를 쓰러뜨리면 공격 +2 (6세대)
        if (move.Id == FellStingerMoveId && target.Hp == 0 && user.Hp > 0)
            ChangeStage(room, attacker, Stat.Attack, 2, announceFailure: false);
        SendStatEffects(room, attacker, user, userStages, user.FocusEnergy, move.TypeId);
        SendStatEffects(room, defender, target, targetStages, target.FocusEnergy, move.TypeId);
        return true;
    }

    // 연속기의 타격 횟수 (DB의 min_hits~max_hits). 연속기가 아니면 1
    private int RollHitCount(PokemonMoveInfo move)
    {
        if (data.MoveMetaById.GetValueOrDefault(move.Id) is not { MinHits: int min, MaxHits: int max })
            return 1;
        return min == 2 && max == 5 ? TwoToFiveHits[Random.Shared.Next(TwoToFiveHits.Length)] : Random.Shared.Next(min, max + 1);
    }

    // 공격기의 능력치 변화 추가 효과 (DB의 MoveStatChange, target은 self 또는 opponent).
    // 한 기술의 변화는 확률(chance)을 한 번만 판정해 함께 적용한다.
    private (Stat Stat, int Change, int Chance)[] GetAttackStatChanges(PokemonMoveInfo move, string target)
    {
        if (move.Id == RapidSpinMoveId)
            return [];
        return data.MoveStatChangesByMoveId[move.Id]
            .Where(change => change.Target == target)
            .Select(change => (Stat: StatByIdentifier[change.Stat],
                Change: move.Id == DiamondStormMoveId ? 1 : change.Change, change.Chance))
            .OrderBy(change => change.Stat)
            .ToArray();
    }

    // 맞은 상대에게 거는 추가 효과: 랭크 초기화 → 얼음 녹이기 → 상태이상 → 능력치 하락 → 풀죽음
    // 추가 효과는 실패해도 알리지 않는다.
    private void ApplyTargetEffects(GameRoom room, BattleSide attacker, BattleSide defender, PokemonMoveInfo move)
    {
        BattlePokemon target = defender.Current;
        // 클리어스모그: 맞은 상대의 랭크를 모두 원래대로 되돌린다.
        if (move.Id == ClearSmogMoveId && target.Stages.Any(stage => stage != 0))
        {
            Array.Clear(target.Stages);
            LogPokemon(room, defender, $"{target.Info.Name}의 능력 변화가 원래대로 돌아왔다!");
        }

        // 얼어 있는 포켓몬은 불꽃 타입 공격이나 열탕·스팀버스트(6세대)를 맞으면 녹는다.
        if (target.Status == Freeze &&
            (HasTypeName(move.TypeId, "불꽃") || move.Id is ScaldMoveId or SteamEruptionMoveId))
        {
            target.Status = null;
            LogPokemon(room, defender, $"{target.Info.Name}의 얼음이 녹았다!");
        }
        ApplyStatusEffects(room, attacker, defender, move, null, announceFailure: false);

        // 흰안개가 있으면 능력치 하락이 막힌다.
        var changes = GetAttackStatChanges(move, "opponent");
        if (changes.Length > 0 && defender.MistTurns == 0 && Random.Shared.Next(100) < changes[0].Chance)
            foreach (var (stat, change, _) in changes)
                ChangeStage(room, defender, stat, change, announceFailure: false);

        // 풀죽음: 이번 턴에 아직 행동하지 않았다면 행동하지 못한다. (이미 행동했으면 턴이 끝날 때 풀린다)
        if (Random.Shared.Next(100) < (data.MoveMetaById.GetValueOrDefault(move.Id)?.FlinchChance ?? 0))
            target.Flinched = true;

        if (BindingMoveIds.Contains(move.Id))
            Bind(room, attacker, defender, move);
        ApplyPowerMoveEffects(room, attacker, defender, move);
        if (move.Id == SmackDownMoveId)
            SmackDown(room, attacker, defender);
        // 드래곤테일·배대뒤치기: 맞은 상대를 강제로 교체한다.
        if (move.Id is DragonTailMoveId or CircleThrowMoveId)
            ForceSwitch(room, defender);
    }

    // 기술을 쓴 쪽에게 오는 추가 효과: 능력치 변화 → HP 흡수 → 반동
    // dealt: 대타를 포함해 실제로 준 데미지. 흡수·반동은 이 값의 drain%(DB, 음수면 반동)이다.
    private void ApplyUserEffects(GameRoom room, BattleSide attacker, PokemonMoveInfo move, int dealt)
    {
        BattlePokemon user = attacker.Current;
        var changes = GetAttackStatChanges(move, "self");
        if (changes.Length > 0 && Random.Shared.Next(100) < changes[0].Chance)
            foreach (var (stat, change, _) in changes)
                ChangeStage(room, attacker, stat, change, announceFailure: false);

        int drain = data.MoveMetaById.GetValueOrDefault(move.Id)?.Drain ?? 0;
        if (drain == 0 || dealt == 0)
            return;
        int amount = Math.Max(1, (int)Math.Round(dealt * Math.Abs(drain) / 100.0, MidpointRounding.AwayFromZero));
        if (drain > 0)
            Heal(room, attacker, amount);
        else
        {
            LogPokemon(room, attacker, $"{WithJosa(user.Info.Name, "은", "는")} 반동으로 데미지를 입었다! ({amount} 데미지)");
            DealDamage(room, attacker, amount);
        }
    }

    private void SendMoveEffect(GameRoom room, BattleSide attacker, PokemonMoveInfo move, double effectiveness)
    {
        string color = data.TypesById[move.TypeId].ColorRgb;
        string type = data.TypesById[move.TypeId].Name;
        room.Host.QueueMessage(FormattableString.Invariant(
            $"BATTLE_EFFECT {move.TypeId} {color} {type} {(attacker == room.HostSide ? "SELF" : "OPPONENT")} {effectiveness}"));
        room.Guest!.QueueMessage(FormattableString.Invariant(
            $"BATTLE_EFFECT {move.TypeId} {color} {type} {(attacker == room.GuestSide ? "SELF" : "OPPONENT")} {effectiveness}"));
    }

    // reflected: 매직코트로 되돌아온 기술 (기술 이름 기록은 이미 남겼다)
    // slot: 이 기술의 슬롯 (흉내내기·스케치가 바꿀 슬롯)
    private void UseStatusMove(GameRoom room, BattleSide attacker, BattleSide defender, PokemonMoveInfo move,
        bool movesLast, bool reflected = false, int slot = 0)
    {
        BattlePokemon user = attacker.Current;
        string head = $"{user.Info.Name}의 {move.Name}!";
        bool causesStatus = data.MoveStatusEffectsByMoveId[move.Id].Count > 0;
        bool changesStats = TryGetStatChanges(move, out var statChanges);
        // 상태이상·능력치 변화·회복·방어·특수 효과가 없는 변화 기술은 아직 구현되지 않았다.
        if (!causesStatus && !changesStats && !SpecialMoveIds.Contains(move.Id) && !HealMoveIds.Contains(move.Id) &&
            !ProtectMoveIds.Contains(move.Id) && !StatusCureMoveIds.Contains(move.Id) && !FieldMoveIds.Contains(move.Id) &&
            !SwitchMoveIds.Contains(move.Id) && !EffectMoveIds.Contains(move.Id) && !CopyMoveIds.Contains(move.Id))
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
        // 타입 면역은 명중 판정보다 먼저 본다: 전기자석파는 땅 타입, 가루·포자 기술은 풀 타입(6세대)에게 효과가 없다.
        if ((move.Id == ThunderWaveMoveId && TypeMultiplier(move, defender.Current, room) == 0) ||
            (PowderMoveIds.Contains(move.Id) && HasType(defender.Current, "풀")))
        {
            LogBoth(room, attacker, "효과가 없는 것 같다...");
            return;
        }
        // 명중률이 있는 기술(상대를 노리는 기술)만 명중 판정을 한다. 독 타입이 쓰는 맹독은 반드시 맞는다(6세대).
        bool alwaysHits = move.Id == ToxicMoveId && HasType(user, "독");
        if (!alwaysHits && !AccuracyCheck(room, user, defender.Current, move))
        {
            LogBoth(room, attacker, "하지만 빗나갔다!");
            return;
        }
        // 대타가 있으면 상대를 노린 변화 기술은 실패한다. 소리 기술은 대타를 무시한다(6세대).
        // 소리 기술과 DB 플래그 authentic인 기술(사슬묶기·앙코르·도발 등)은 대타를 무시한다(6세대).
        if ((IsOpponentStatusMove(move) || HasFlag(move, "protect")) && defender.Current.SubstituteHp > 0 &&
            !BypassesSubstitute(move))
        {
            LogBoth(room, attacker, "하지만 실패했다!");
            return;
        }

        BattlePokemon target = defender.Current;
        int[] userStages = user.Stages.ToArray(), targetStages = target.Stages.ToArray();
        bool userFocusEnergy = user.FocusEnergy, targetFocusEnergy = target.FocusEnergy;
        int[] buffsBefore = GetBuffLevels(attacker);
        // 성장: 쾌청이면 공격·특수공격이 2단계씩 오른다.
        if (move.Id == GrowthMoveId && room.Weather == Sun)
            statChanges = statChanges.Select(change => (change.Stat, change.Change * 2, change.Target)).ToArray();
        // 베놈트랩: 독·맹독 상태인 상대에게만 성공한다.
        if (move.Id == VenomDrenchMoveId && target.Status is not (Poison or BadPoison))
            LogBoth(room, attacker, "하지만 실패했다!");
        else if (changesStats)
        {
            UseStatChangeMove(room, attacker, defender, statChanges);
            if (move.Id == DefenseCurlMoveId)
                user.DefenseCurled = true;  // 웅크리기: 구르기·아이스볼 위력 2배
        }
        else if (FieldMoveIds.Contains(move.Id))
            UseFieldMove(room, attacker, move);
        else if (SwitchMoveIds.Contains(move.Id))
            UseSwitchMove(room, attacker, defender, move);
        else if (EffectMoveIds.Contains(move.Id))
            UseEffectMove(room, attacker, defender, move);
        else if (CopyMoveIds.Contains(move.Id))
            UseCopyMove(room, attacker, defender, move, slot);
        else if (SpecialMoveIds.Contains(move.Id))
            UseSpecialMove(room, attacker, defender, move);
        else if (HealMoveIds.Contains(move.Id))
            UseHealMove(room, attacker, defender, move);
        else if (ProtectMoveIds.Contains(move.Id))
            UseProtectMove(room, attacker, move, movesLast);
        else if (StatusCureMoveIds.Contains(move.Id))
            UseStatusCureMove(room, attacker, defender, move);
        bool userRankRaised = SendStatEffects(room, attacker, user, userStages, userFocusEnergy, move.TypeId);
        SendStatEffects(room, defender, target, targetStages, targetFocusEnergy, move.TypeId);
        if (!userRankRaised && ReferenceEquals(attacker.Current, user) && HasNewBuff(attacker, buffsBefore))
        {
            attacker.Player.QueueMessage($"BATTLE_BUFF {move.TypeId} SELF");
            defender.Player.QueueMessage($"BATTLE_BUFF {move.TypeId} OPPONENT");
        }
        if (causesStatus)
            ApplyStatusEffects(room, attacker, defender, move, null, announceFailure: true);
    }

    // 치료방울·아로마테라피: 교체로 들어가 있는 포켓몬까지 우리 팀 전체의 상태이상을 고친다. (혼란·헤롱헤롱은 그대로)
    // 사이코시프트: 자신의 상태이상을 상대에게 옮기고 자신은 낫는다.
    private void UseStatusCureMove(GameRoom room, BattleSide attacker, BattleSide defender, PokemonMoveInfo move)
    {
        BattlePokemon user = attacker.Current, target = defender.Current;
        // 리프레시: 자신의 독·맹독·마비·화상을 고친다.
        if (move.Id == RefreshMoveId)
        {
            if (user.Status is not (Poison or BadPoison or Paralysis or Burn))
            {
                LogBoth(room, attacker, "하지만 실패했다!");
                return;
            }
            LogPokemon(room, attacker, $"{user.Info.Name}의 {StatusName(user.Status)} 상태가 나았다!");
            user.Status = null;
            SendHealEffect(room, attacker);
            return;
        }
        if (move.Id == PsychoShiftMoveId)
        {
            // 상대에게 이미 상태이상이 있거나 대타가 있으면 실패한다. 타입 면역·신비의부적은 TryInflict가 막는다.
            if (user.Status is not string code || target.Status != null || target.SubstituteHp > 0)
            {
                LogBoth(room, attacker, "하지만 실패했다!");
                return;
            }
            TryInflict(room, defender, code, announceFailure: true, fromOpponent: true);
            if (target.Status == null)
                return;
            user.Status = null;
            LogPokemon(room, attacker, $"{user.Info.Name}의 {StatusName(code)} 상태가 나았다!");
            SendHealEffect(room, attacker);
            return;
        }

        BattlePokemon[] cured = attacker.Team.Where(member => member.Hp > 0 && member.Status != null).ToArray();
        if (cured.Length == 0)
        {
            LogBoth(room, attacker, "하지만 실패했다!");
            return;
        }
        LogBoth(room, attacker, move.Id == HealBellMoveId ? "방울소리가 울려 퍼졌다!" : "기분 좋은 향기가 퍼졌다!");
        foreach (BattlePokemon member in cured)
        {
            LogPokemon(room, attacker, $"{member.Info.Name}의 {StatusName(member.Status!)} 상태가 나았다!");
            member.Status = null;
        }
        SendHealEffect(room, attacker);
    }

    // 미스트필드·일렉트릭필드에서 땅에 있으면 잠들 수 없어서 실패한다.
    private void UseRest(GameRoom room, BattleSide side)
    {
        BattlePokemon user = side.Current;
        if (user.Hp == user.MaxHp || user.Status == Sleep || FieldBlocksStatus(room, user, Sleep) != null)
        {
            LogBoth(room, side, "하지만 실패했다!");
            return;
        }
        // 다른 상태이상이 있어도 잠듦으로 바뀐다. 두 번 행동하지 못하고 세 번째에 깨어나 행동한다.
        user.Status = Sleep;
        user.SleepTurns = 3;
        user.Hp = user.MaxHp;
        SendStatusEffect(room, side, Sleep);
        LogPokemon(room, side, $"{WithJosa(user.Info.Name, "은", "는")} 잠들어서 건강해졌다!");
        SendHealEffect(room, side);
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
                if (success && BlockedByField(room, side, code, announceFailure))
                    return;
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
                if (success && BlockedByField(room, side, code, announceFailure))
                    return;
                if (success)
                {
                    target.Status = code;
                    if (code == Sleep) target.SleepTurns = Random.Shared.Next(2, 5);
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
        {
            SendStatusEffect(room, side, code);
            LogPokemon(room, side, $"{WithJosa(target.Info.Name, "은", "는")} {text}");
        }
        else if (announceFailure)
            LogBoth(room, side, "하지만 실패했다!");
    }

    // 날씨·필드가 상태이상을 막으면(FieldBlocksStatus) 알리고 true
    private bool BlockedByField(GameRoom room, BattleSide side, string code, bool announceFailure)
    {
        if (FieldBlocksStatus(room, side.Current, code) is not string reason)
            return false;
        if (announceFailure)
        {
            if (reason.Length > 0)
                LogPokemon(room, side, reason);
            else
                LogBoth(room, side, "하지만 실패했다!");
        }
        return true;
    }

    // 하품: 다음 턴이 끝날 때 잠든다. 신비의부적이 있거나 미스트필드·일렉트릭필드에서 땅에 있으면 실패한다.
    private void TryMakeDrowsy(GameRoom room, BattleSide side, bool announceFailure)
    {
        BattlePokemon target = side.Current;
        if (target.Status != null || target.DrowsyTurns > 0 || side.SafeguardTurns > 0)
        {
            if (announceFailure) LogBoth(room, side, "하지만 실패했다!");
            return;
        }
        if (BlockedByField(room, side, Sleep, announceFailure))
            return;
        target.DrowsyTurns = 2;
        SendStatusEffect(room, side, Sleep);
        LogPokemon(room, side, $"{WithJosa(target.Info.Name, "은", "는")} 졸음이 오기 시작했다!");
    }

    // 6세대 타입 면역: 전기는 마비, 불꽃은 화상, 얼음은 얼음, 독·강철은 독과 맹독에 걸리지 않는다.
    private bool IsImmune(BattlePokemon pokemon, string code) => code switch
    {
        Paralysis => HasType(pokemon, "전기"),
        Burn => HasType(pokemon, "불꽃"),
        Freeze => HasType(pokemon, "얼음"),
        Poison or BadPoison => HasType(pokemon, "독") || HasType(pokemon, "강철"),
        _ => false
    };

    // 기술을 쓰기 전에 잠듦 → 얼음 → 풀죽음 → 혼란 → 헤롱헤롱 → 마비 순서로 행동할 수 있는지 판정한다.
    private bool CanAct(GameRoom room, BattleSide side, PokemonMoveInfo move)
    {
        BattlePokemon user = side.Current;
        string subject = WithJosa(user.Info.Name, "은", "는");

        if (user.Status == Sleep)
        {
            if (--user.SleepTurns > 0)
            {
                SendStatusEffect(room, side, Sleep);
                LogPokemon(room, side, $"{subject} 쿨쿨 잠들어 있다.");
                if (move.Id is not (SnoreMoveId or SleepTalkMoveId))    // 코골기·잠꼬대는 잠든 채로 쓸 수 있다.
                    return false;
            }
            else
            {
                user.Status = null;
                LogPokemon(room, side, $"{subject} 눈을 떴다!");
            }
        }

        if (user.Status == Freeze)
        {
            if (!SelfThawMoveIds.Contains(move.Id) && Random.Shared.Next(100) >= 20)
            {
                SendStatusEffect(room, side, Freeze);
                LogPokemon(room, side, $"{subject} 얼어붙어서 움직일 수 없다!");
                return false;
            }
            user.Status = null;
            LogPokemon(room, side, $"{user.Info.Name}의 얼음이 녹았다!");
        }

        if (user.Flinched)
        {
            LogPokemon(room, side, $"{subject} 풀이 죽어 기술을 쓸 수 없다!");
            return false;
        }

        if (user.ConfusionTurns > 0)
        {
            if (--user.ConfusionTurns == 0)
                LogPokemon(room, side, $"{user.Info.Name}의 혼란이 풀렸다!");
            else
            {
                SendStatusEffect(room, side, Confusion);
                LogPokemon(room, side, $"{subject} 혼란에 빠져 있다!");
                // 6세대까지는 50% 확률로 자신을 공격한다.
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
            SendStatusEffect(room, side, Infatuation);
            LogPokemon(room, side, $"{subject} 헤롱헤롱해 있다!");
            if (Random.Shared.Next(2) == 0)
            {
                LogPokemon(room, side, $"{subject} 헤롱헤롱해서 기술을 쓸 수 없었다!");
                return false;
            }
        }

        if (user.Status == Paralysis && Random.Shared.Next(4) == 0)
        {
            SendStatusEffect(room, side, Paralysis);
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

    // 양쪽 행동이 끝난 뒤: 날씨(모래바람·싸라기눈 데미지) → 미래예지 → 그래스필드 회복
    // → 회복(희망사항·아쿠아링·뿌리박기) → 화상·독·맹독 데미지 → 묶기 데미지 → 하품으로 잠들기 → 프리폴 풀기
    // → 우리 편 효과 턴 감소 → 필드 효과 턴 감소
    private void EndOfTurn(GameRoom room)
    {
        IEnumerable<BattleSide> sides = new[] { room.HostSide!, room.GuestSide! }
            .OrderByDescending(GetSpeed).ToList();

        ClearTurnProtection(room);          // 방어·버티기·매직코트·패스트가드는 이번 턴까지만
        foreach (BattleSide side in sides)
            side.Current.Roosting = false;   // 날개쉬기로 없어진 비행 타입은 이번 턴까지만
        TickWeather(room, sides);
        TickFutureAttacks(room);            // 미래예지·파멸의소원
        GrassyTerrainHealing(room, sides);
        EndOfTurnHealing(room, sides);
        TickLeechSeed(room, sides);

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
            SendStatusEffect(room, side, pokemon.Status);
            LogPokemon(room, side, $"{WithJosa(pokemon.Info.Name, "은", "는")} {cause} 데미지를 입었다! ({damage} 데미지)");
            DealDamage(room, side, damage);
            SendBattleState(room);
        }

        TickBinding(room, sides);
        TickEffects(room, sides);   // 악몽·저주·멸망의노래·기술 제한 등의 남은 턴

        foreach (BattleSide side in sides)
        {
            BattlePokemon pokemon = side.Current;
            if (pokemon.Hp > 0 && pokemon.DrowsyTurns > 0 && --pokemon.DrowsyTurns == 0)
                TryInflict(room, side, Sleep, announceFailure: false, fromOpponent: true);
        }

        ReleaseSkyDrop(room);
        TickSideEffects(room);
        TickFields(room);
    }

    // HP를 줄이고, 쓰러지면 Faint로 처리한다.
    private static void DealDamage(GameRoom room, BattleSide side, int damage)
    {
        BattlePokemon pokemon = side.Current;
        if (pokemon.Hp == 0)
            return;
        pokemon.Hp = Math.Max(0, pokemon.Hp - damage);
        pokemon.HurtThisTurn |= damage > 0;
        if (pokemon.Hp == 0)
            Faint(room, side);
    }

    // 쓰러졌다는 기록을 남기고, 남은 포켓몬이 있으면 교체를 요구한다.
    private static void Faint(GameRoom room, BattleSide side)
    {
        side.FaintedThisTurn = true;
        LogPokemon(room, side, $"{WithJosa(side.Current.Info.Name, "이", "가")} 쓰러졌다!");
        if (side.Team.Any(member => member.Hp > 0))
            side.MustSwitch = true;
    }

    // 연출 대상 기준으로 양쪽 클라이언트의 SELF/OPPONENT를 지정한다.
    private static void SendStatusEffect(GameRoom room, BattleSide side, string code)
    {
        side.Player.QueueMessage($"BATTLE_STATUS {code} SELF");
        GetOther(room, side).Player.QueueMessage($"BATTLE_STATUS {code} OPPONENT");
    }

    // 타입 상성 배율 (지금 타입 기준). room을 주면 땅에 있는지도 반영한다:
    // 중력·떨어뜨리기·뿌리박기 중에는 비행 타입도 땅 기술을 맞고, 전자부유·텔레키네시스 중에는 땅 기술을 맞지 않는다.
    // 꿰뚫어보기·냄새구별은 고스트 타입, 미라클아이는 악 타입의 무효를 없앤다.
    private double TypeMultiplier(PokemonMoveInfo move, BattlePokemon defender, GameRoom? room = null)
    {
        bool groundMove = HasTypeName(move.TypeId, "땅");
        bool forcedGround = room != null && (room.GravityTurns > 0 || defender.SmackedDown || defender.Ingrained);
        if (groundMove && !forcedGround && (defender.MagnetRiseTurns > 0 || defender.TelekinesisTurns > 0))
            return 0;
        double multiplier = 1;
        foreach (int typeId in defender.Types)
        {
            // 날개쉬기를 쓴 턴에는 비행 타입이 없다. (비행 단일 타입이면 상성 1배)
            if ((defender.Roosting || (forcedGround && groundMove)) && HasTypeName(typeId, "비행"))
                continue;
            // 프리즈드라이: 물 타입에게 효과가 굉장하다.
            if (move.Id == FreezeDryMoveId && HasTypeName(typeId, "물"))
            {
                multiplier *= 2;
                continue;
            }
            double factor = data.GetMultiplier(move.TypeId, typeId);
            if (factor == 0 && ((defender.Identified && HasTypeName(typeId, "고스트")) ||
                                (defender.MiracleEye && HasTypeName(typeId, "악"))))
                continue;
            multiplier *= factor;
        }
        return multiplier;
    }

    private bool HasTypeName(int typeId, string name) => data.TypesById[typeId].Name == name;

    // 지금 타입(물붓기 등으로 바뀐 타입 포함) 중에 있는지
    private bool HasType(BattlePokemon pokemon, string typeName) => pokemon.Types.Any(type => HasTypeName(type, typeName));

    private string StatusName(string code) => data.StatusConditionsById.Values.First(s => s.Code == code).Name;

    // owner 팀의 포켓몬 이야기: owner에게는 그대로, 상대에게는 "상대 "를 붙여 보낸다.
    private static void LogPokemon(GameRoom room, BattleSide owner, string text) =>
        Log(room, owner, text, "상대 " + text);

    private static void LogBoth(GameRoom room, BattleSide side, string text) => Log(room, side, text, text);
}
