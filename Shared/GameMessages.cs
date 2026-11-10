namespace Pokemon.Protocol;

public sealed record TypeView(int TypeId, string Name, string ColorRgb);
public sealed record MoveView(int MoveId, string Name, TypeView Type, int? Power, int? Accuracy);
// PokemonId: 폼 번호 (기본 폼은 전국도감 번호, 리전폼 등은 10001 이후). SpeciesId: 전국도감 번호
// Status: 교체해도 남는 상태이상의 한국어 이름(마비, 잠듦, 얼음, 화상, 독, 맹독). 없으면 null
// StatChanges: 0이 아닌 능력치 랭크와 기충전. 예: ["공격 +2", "스피드 -1", "기충전"]
public sealed record PokemonView(int PokemonId, int SpeciesId, string Name, int CurrentHp, int MaxHp, TypeView[] Types,
    string? Status, string[]? StatChanges);
// FormName: 같은 종의 다른 폼과 구분하는 이름 (알로라의 모습, 어택폼 등). 구분할 필요가 없으면 null
public sealed record PokemonCatalogEntry(int PokemonId, int SpeciesId, string Name, string? FormName,
    TypeView[] Types, int[] LearnableMoveIds);
public sealed record PokemonCatalogResponse(PokemonCatalogEntry[] Pokemon, MoveView[] Moves);
public sealed record PokemonSelection(int PokemonId, int[] MoveIds);

// 로비의 방 목록 한 줄. 방 이름은 "<HostName>의 방"으로 표시한다.
// PlayerCount가 2이거나 InGame이면 들어갈 수 없다.
public sealed record RoomSummary(int RoomId, string HostName, int PlayerCount, bool InGame);

// 전투 화면 정보. 받는 플레이어 기준으로 내 팀 전체와 상대의 현재 출전 포켓몬만 담는다.
public sealed record TeamMemberView(PokemonView Pokemon, MoveView[] Moves);
public sealed record BattleView(TeamMemberView[] Team, int Active, PokemonView Opponent);
