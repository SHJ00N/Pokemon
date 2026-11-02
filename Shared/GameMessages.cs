namespace Pokemon.Protocol;

public sealed record TypeView(int TypeId, string Name, string ColorRgb);
public sealed record MoveView(int MoveId, string Name, TypeView Type, int? Power, int? Accuracy);
// Status: 교체해도 남는 상태이상의 한국어 이름(마비, 잠듦, 얼음, 화상, 독, 맹독). 없으면 null
// StatChanges: 0이 아닌 능력치 랭크와 기충전. 예: ["공격 +2", "스피드 -1", "기충전"]
public sealed record PokemonView(int PokemonId, string Name, int CurrentHp, int MaxHp, TypeView[] Types,
    string? Status, string[]? StatChanges);
public sealed record PokemonCatalogEntry(int PokemonId, string Name, TypeView[] Types, int[] LearnableMoveIds);
public sealed record PokemonCatalogResponse(PokemonCatalogEntry[] Pokemon, MoveView[] Moves);
public sealed record PokemonSelection(int PokemonId, int[] MoveIds);

// 전투 화면 정보. 받는 플레이어 기준으로 내 팀 전체와 상대의 현재 출전 포켓몬만 담는다.
public sealed record TeamMemberView(PokemonView Pokemon, MoveView[] Moves);
public sealed record BattleView(TeamMemberView[] Team, int Active, PokemonView Opponent);
