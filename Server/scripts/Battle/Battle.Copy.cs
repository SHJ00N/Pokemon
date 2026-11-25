// 타입을 바꾸거나 다른 기술·포켓몬을 따라 하는 기술. 수치는 6세대 규칙을 따른다.
// 타입 변경(텍스처·텍스처2·보호색·물붓기·미러타입·핼러윈·숲의저주), 변신, 흉내내기·스케치,
// 다른 기술을 쓰는 기술(손가락흔들기·따라하기·흉내쟁이·조수·선취·자연의힘)
partial class Battle
{
    private const int ConversionMoveId = 160, Conversion2MoveId = 176, CamouflageMoveId = 293, SoakMoveId = 487,
        ReflectTypeMoveId = 513, TrickOrTreatMoveId = 567, ForestsCurseMoveId = 571, TransformMoveId = 144,
        MimicMoveId = 102, SketchMoveId = 166;
    private const int MetronomeMoveId = 118, MirrorMoveMoveId = 119, CopycatMoveId = 383, AssistMoveId = 274,
        MeFirstMoveId = 382, NaturePowerMoveId = 267;

    // UseCopyMove가 처리하는 변화 기술 (다른 기술을 쓰는 기술은 ExecuteMove에서 PickCalledMove로 처리)
    private static readonly HashSet<int> CopyMoveIds =
    [
        ConversionMoveId, Conversion2MoveId, CamouflageMoveId, SoakMoveId, ReflectTypeMoveId, TrickOrTreatMoveId,
        ForestsCurseMoveId, TransformMoveId, MimicMoveId, SketchMoveId
    ];
    private static readonly HashSet<int> CallingMoveIds =
        [MetronomeMoveId, MirrorMoveMoveId, CopycatMoveId, AssistMoveId, MeFirstMoveId, NaturePowerMoveId];

    // 다른 기술을 쓰는 기술로 나오지 않는 기술 (6세대 손가락흔들기·조수·흉내쟁이 금지 목록을 합친 것):
    // 다른 기술을 부르거나 따라 하는 기술, 방어 계열, 도구를 쓰는 기술, 상대 행동에 반응하는 기술, 전용 기술 등.
    // 이 게임에서는 여러 턴 이어서 쓰는 구르기·아이스볼·참기·소란피기도 뺀다.
    private static readonly HashSet<int> CallBannedMoveIds =
    [
        495, 274, 448, 383, 68, 343, 194, 197, 203, 364, 264, 266, 553, 270, 554, 588, 382, 118, 102, 243, 119,
        267, 182, 511, 501, 476, 547, 548, 166, 214, 555, 289, 173, 596, 165, 546, 168, 144, 557, 469,
        509, 525, 46, 18, 340, 91, 291, 19, 566, 467, 507, 591, 592, 593, 614, 615, 617, 621,
        205, 301, 117, 253
    ];
    // 따라 할 수 없는 기술 (흉내내기·스케치): 흉내내기, 스케치, 변신, 발버둥, 손가락흔들기, 수다
    private static readonly HashSet<int> MimicBannedMoveIds = [MimicMoveId, SketchMoveId, TransformMoveId, StruggleMoveId, MetronomeMoveId, 448];

    // slot: 이 기술의 슬롯 (흉내내기·스케치가 바꿀 슬롯)
    private void UseCopyMove(BattleSide attacker, BattleSide defender, PokemonMoveInfo move, int slot)
    {
        BattlePokemon user = attacker.Current, target = defender.Current;
        string userSubject = WithJosa(user.Info.Name, "은", "는");
        switch (move.Id)
        {
            case ConversionMoveId:
                // 첫 번째 기술의 타입이 된다(6세대).
                ChangeTypes(attacker, user, [user.Moves[0].Move.TypeId]);
                break;
            case Conversion2MoveId:
                // 상대가 마지막으로 쓴 기술에 강한(반감·무효) 타입 중 하나가 된다.
                int[] resist = target.LastExecutedMove is PokemonMoveInfo last
                    ? data.TypesById.Keys.Where(type => !user.Types.Contains(type) &&
                                                        data.GetMultiplier(last.TypeId, type) < 1).ToArray()
                    : [];
                if (resist.Length == 0)
                    LogBoth(attacker, "하지만 실패했다!");
                else
                    ChangeTypes(attacker, user, [resist[Random.Next(resist.Length)]]);
                break;
            case CamouflageMoveId:
                // 필드에 따라 바뀐다: 없음(실내) 노말, 일렉트릭필드 전기, 그래스필드 풀, 미스트필드 페어리
                ChangeTypes(attacker, user, [TypeIdOf(Terrain switch
                {
                    ElectricTerrain => "전기", GrassyTerrain => "풀", MistyTerrain => "페어리", _ => "노말"
                })]);
                break;
            case SoakMoveId:
                ChangeTypes(defender, target, [TypeIdOf("물")]);
                break;
            case ReflectTypeMoveId:
                ChangeTypes(attacker, user, target.Types.ToArray());
                break;
            case TrickOrTreatMoveId:
            case ForestsCurseMoveId:
                // 세 번째 타입을 더한다 (이미 그 타입이면 실패). 핼러윈은 고스트, 숲의저주는 풀
                string added = move.Id == TrickOrTreatMoveId ? "고스트" : "풀";
                if (HasType(target, added))
                {
                    LogBoth(attacker, "하지만 실패했다!");
                    break;
                }
                target.AddedType = TypeIdOf(added);
                LogPokemon(defender, $"{target.Info.Name}에게 {added} 타입이 추가되었다!");
                break;

            case TransformMoveId:
                // 상대에게 대타가 있거나 어느 한쪽이 이미 변신했으면 실패한다.
                if (user.Transformed || target.Transformed || target.SubstituteHp > 0)
                {
                    LogBoth(attacker, "하지만 실패했다!");
                    break;
                }
                user.TransformInto(target);
                LogPokemon(attacker, $"{userSubject} 상대 {WithJosa(target.Info.Name, "으로", "로")} 변신했다!");
                break;

            case MimicMoveId:
            case SketchMoveId:
                // 상대가 마지막으로 쓴 기술을 이 슬롯에 배운다. 흉내내기는 교체할 때까지, 스케치는 계속
                if ((move.Id == SketchMoveId && user.Transformed) ||
                    target.LastExecutedMove is not PokemonMoveInfo copied || MimicBannedMoveIds.Contains(copied.Id) ||
                    user.Moves.Any(known => known.Move.Id == copied.Id))
                {
                    LogBoth(attacker, "하지만 실패했다!");
                    break;
                }
                if (move.Id == SketchMoveId)
                    user.LearnPermanently(slot, copied);
                else
                    user.LearnTemporarily(slot, copied);
                LogPokemon(attacker, $"{userSubject} {WithJosa(copied.Name, "을", "를")} {(move.Id == SketchMoveId ? "스케치했다" : "흉내 냈다")}!");
                break;
        }
    }

    // 타입을 바꾼다(더해진 세 번째 타입은 없어진다). 이미 그 타입이면 실패
    private void ChangeTypes(BattleSide side, BattlePokemon pokemon, int[] types)
    {
        if (pokemon.AddedType == null && pokemon.BaseTypes.Order().SequenceEqual(types.Order()))
        {
            LogBoth(side, "하지만 실패했다!");
            return;
        }
        pokemon.BaseTypes = types;
        pokemon.AddedType = null;
        string names = string.Join("·", types.Select(type => data.TypesById[type].Name));
        LogPokemon(side, $"{WithJosa(pokemon.Info.Name, "은", "는")} {names} 타입이 되었다!");
    }

    // 다른 기술을 쓰는 기술이 쓸 기술. 쓸 기술이 없으면 실패를 알리고 null
    private PokemonMoveInfo? PickCalledMove(BattleSide attacker, BattleSide defender, PokemonMoveInfo move)
    {
        BattlePokemon user = attacker.Current, target = defender.Current;
        LogPokemon(attacker, $"{user.Info.Name}의 {move.Name}!");
        bool Allowed(PokemonMoveInfo candidate) => !CallBannedMoveIds.Contains(candidate.Id) && candidate.Generation <= 6;
        PokemonMoveInfo? called = null;
        switch (move.Id)
        {
            case MetronomeMoveId:
                // 6세대까지의 기술 중 무작위
                PokemonMoveInfo[] pool = data.MovesById.Values.Where(Allowed).ToArray();
                called = pool[Random.Next(pool.Length)];
                break;
            case MirrorMoveMoveId:
                // 상대가 마지막으로 쓴 기술 (따라 할 수 있는 기술만)
                if (target.LastExecutedMove is PokemonMoveInfo mirrored && HasFlag(mirrored, "mirror") && Allowed(mirrored))
                    called = mirrored;
                break;
            case CopycatMoveId:
                // 전투에서 마지막으로 나온 기술
                if (LastExecutedMove is PokemonMoveInfo copied && Allowed(copied))
                    called = copied;
                break;
            case AssistMoveId:
                // 우리 팀 다른 포켓몬의 기술 중 무작위
                PokemonMoveInfo[] team = attacker.Team.Where(member => !ReferenceEquals(member, user))
                    .SelectMany(member => member.Moves).Select(slot => slot.Move).Where(Allowed).ToArray();
                if (team.Length > 0)
                    called = team[Random.Next(team.Length)];
                break;
            case MeFirstMoveId:
                // 상대가 이번 턴에 아직 행동하지 않았고 공격 기술을 골랐으면 그 기술을 위력 1.5배로 먼저 쓴다.
                if (!target.MovedThisTurn && defender.ChosenMove is int targetSlot && target.RechargeSlot == null &&
                    !defender.Struggling && target.Moves[targetSlot - 1].Move is { Category: not "변화" } chosen && Allowed(chosen))
                    called = chosen with { Power = chosen.Power * 3 / 2 };
                break;
            case NaturePowerMoveId:
                // 필드에 따라 바뀐다: 없음 트라이어택, 일렉트릭필드 10만볼트, 그래스필드 에너지볼, 미스트필드 문포스
                called = data.MovesById[Terrain switch
                {
                    ElectricTerrain => 85, GrassyTerrain => 412, MistyTerrain => 585, _ => 161
                }];
                break;
        }
        if (called == null)
            LogBoth(attacker, "하지만 실패했다!");
        return called;
    }
}
