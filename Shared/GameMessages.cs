namespace Pokemon.Protocol;

public sealed record TypeView(int TypeId, string Name, string ColorRgb);
public sealed record MoveView(int MoveId, string Name, TypeView Type, int? Power, int? Accuracy);
public sealed record PokemonView(int PokemonId, string Name, int CurrentHp, int MaxHp, TypeView[] Types);
public sealed record PokemonCatalogEntry(int PokemonId, string Name, TypeView[] Types, int[] LearnableMoveIds);
public sealed record PokemonCatalogResponse(PokemonCatalogEntry[] Pokemon, MoveView[] Moves);
public sealed record PokemonSelection(int PokemonId, int[] MoveIds);
public sealed record BattleView(PokemonView Player, PokemonView Opponent, MoveView[] Moves);
