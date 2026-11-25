using System.Text.Json;
using Pokemon.Protocol;

// 화면 정보(BattleView 등) 만들기. 타입·기술 정보는 로비의 포켓몬 목록(RoomManager)도 같이 쓴다.
partial class Battle
{
    public void SendBattleState(BattleSide? megaEvolved = null)
    {
        foreach (BattleSide side in new[] { HostSide, GuestSide })
        {
            var team = side.Team.Select(p => new TeamMemberView(GetPokemon(p),
                p.Moves.Select(slot => GetMove(slot) with
                {
                    BlockedByGravity = p == side.Current && IsBlockedByGravity(slot.Move)
                }).ToArray()));
            MegaFormView[] megaForms = GetMegaForms(side).Select(form => new MegaFormView(form.Id, form.Name)).ToArray();
            string prefix = megaEvolved == null ? "BATTLE_STATE "
                : megaEvolved == side ? "BATTLE_MEGA SELF " : "BATTLE_MEGA OPPONENT ";
            Send(side, prefix + JsonSerializer.Serialize(
                new BattleView(team.ToArray(), side.Active, GetPokemon(GetOther(side).Current),
                    GetFieldEffects(), megaForms.Length > 0, Weather, Terrain, megaForms,
                    GetSideEffects(side), GetSideEffects(GetOther(side)))));
        }
    }

    internal static TypeView GetType(GameData data, int id)
    {
        PokemonTypeInfo type = data.TypesById[id];
        return new TypeView(type.Id, type.Name, type.ColorRgb);
    }

    internal static TypeView[] GetTypes(GameData data, PokemonInfo pokemon) => pokemon.Type2Id is int second
        ? [GetType(data, pokemon.Type1Id), GetType(data, second)] : [GetType(data, pokemon.Type1Id)];

    internal static MoveView GetMove(GameData data, PokemonMoveInfo move) =>
        new(move.Id, move.Name, GetType(data, move.TypeId), move.Power, move.Accuracy, MaxPp: move.Pp);

    private TypeView GetType(int id) => GetType(data, id);

    private MoveView GetMove(BattleMoveSlot slot) =>
        GetMove(data, slot.Move) with { Pp = slot.Pp, MaxPp = slot.MaxPp };

    private PokemonView GetPokemon(BattlePokemon pokemon) =>
        new(pokemon.Info.Id, pokemon.Info.SpeciesId, pokemon.Info.Name, pokemon.Hp, pokemon.MaxHp,
            pokemon.Types.Select(GetType).ToArray(),    // 물붓기 등으로 바뀐 지금 타입
            pokemon.Status == null ? null : StatusName(pokemon.Status), DescribeStages(pokemon),
            pokemon.TransformedInto is { } form ? new PokemonAppearance(form.Id, form.SpeciesId) : null);
}
