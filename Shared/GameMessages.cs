namespace Pokemon.Protocol;

public sealed record TypeView(int TypeId, string Name, string ColorRgb);
public sealed record MoveView(int MoveId, string Name, TypeView Type, int? Power, int? Accuracy);
public sealed record PokemonView(int PokemonId, string Name, int CurrentHp, int MaxHp, TypeView[] Types);
public sealed record PokemonCatalogEntry(int PokemonId, string Name, TypeView[] Types, int[] LearnableMoveIds);
public sealed record PokemonCatalogResponse(PokemonCatalogEntry[] Pokemon, MoveView[] Moves);
public sealed record PokemonSelection(int PokemonId, int[] MoveIds);
public sealed record BattleView(PokemonView Player, PokemonView Opponent, MoveView[] Moves);

// 기술 한 번의 결과. 받는 플레이어 기준으로 ByPlayer(내가 공격했는지)와 HP가 정해진다.
// Supported가 false면 아직 효과가 구현되지 않은 기술(변화 기술, 위력 없는 기술)이다.
public sealed record MoveResult(bool ByPlayer, string AttackerName, string MoveName, bool Supported, bool Hit,
    int Damage, double Effectiveness, int PlayerHp, int OpponentHp);
