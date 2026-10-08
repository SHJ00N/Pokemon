using System.Collections.Frozen;
using Microsoft.Data.Sqlite;

internal sealed record PokemonTypeInfo(int Id, string Name, string ColorRgb);

internal sealed record PokemonMoveInfo(
    int Id, string Name, int TypeId, string Category,
    int? Power, int? Accuracy, int Pp, int Priority);

internal sealed record PokemonInfo(
    int Id,
    string Name,
    int Type1Id,
    int? Type2Id,
    int BaseHp,
    int BaseAttack,
    int BaseDefense,
    int BaseSpAttack,
    int BaseSpDefense,
    int BaseSpeed);

/* 
db에서 추출한 기본 데이터
테이블 목록 : 포켓몬, 타입, 기술, 타입간 상관 관계, 포켓몬별 기술 목록
*/
internal sealed class GameData
{
    // 실행중 변경되지 않는 데이터이므로 FrozenDictionary 사용
    public FrozenDictionary<int, PokemonInfo> PokemonById { get; }
    public FrozenDictionary<int, PokemonTypeInfo> TypesById { get; }
    public FrozenDictionary<int, PokemonMoveInfo> MovesById { get; }
    public FrozenDictionary<(int AttackTypeId, int DefendTypeId), double> EffectivenessByType { get; }

    public FrozenDictionary<int, FrozenSet<int>> LearnableMovesByPokemonId { get; }

    private GameData(
        FrozenDictionary<int, PokemonInfo> pokemonById,
        FrozenDictionary<int, PokemonTypeInfo> typesById,
        FrozenDictionary<int, PokemonMoveInfo> movesById,
        FrozenDictionary<(int AttackTypeId, int DefendTypeId), double> effectivenessByType,
        FrozenDictionary<int, FrozenSet<int>> learnableMovesByPokemonId)
    {
        PokemonById = pokemonById;
        TypesById = typesById;
        MovesById = movesById;
        EffectivenessByType = effectivenessByType;
        LearnableMovesByPokemonId = learnableMovesByPokemonId;
    }

    public static GameData Load(string databasePath)
    {
        if (!File.Exists(databasePath))
            throw new FileNotFoundException(databasePath + "포켓몬 DB 파일을 찾을 수 없습니다.", databasePath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        FrozenDictionary<int, PokemonTypeInfo> types = LoadTypes(connection);
        FrozenDictionary<int, PokemonInfo> pokemon = LoadPokemons(connection);
        FrozenDictionary<int, PokemonMoveInfo> moves = LoadMoves(connection);
        FrozenDictionary<(int AttackTypeId, int DefendTypeId), double> effectiveness =
            LoadEffectiveness(connection);

        // 데이터 유효성 확인
        foreach (PokemonInfo entry in pokemon.Values)
        {
            if (!types.ContainsKey(entry.Type1Id) ||
                (entry.Type2Id is int type2Id && !types.ContainsKey(type2Id)))
                throw new InvalidDataException($"포켓몬 {entry.Id}의 타입 ID가 유효하지 않습니다.");
        }
        foreach (PokemonMoveInfo entry in moves.Values)
        {
            if (!types.ContainsKey(entry.TypeId))
                throw new InvalidDataException($"기술 {entry.Id}의 타입 ID가 유효하지 않습니다.");
        }
        if (effectiveness.Count != types.Count * types.Count ||
            effectiveness.Keys.Any(pair =>
                !types.ContainsKey(pair.AttackTypeId) || !types.ContainsKey(pair.DefendTypeId)))
            throw new InvalidDataException("타입 상성표에 누락되거나 유효하지 않은 조합이 있습니다.");

        var learnsets = LoadLearnsets(connection, pokemon, moves);
        return new GameData(pokemon, types, moves, effectiveness, learnsets);
    }

    public double GetMultiplier(int attackTypeId, int defendTypeId) =>
        EffectivenessByType[(attackTypeId, defendTypeId)];

    private static FrozenDictionary<int, PokemonInfo> LoadPokemons(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pokemon_id, name, type1_id, type2_id,
                   base_hp, base_attack, base_defense,
                   base_sp_attack, base_sp_defense, base_speed
            FROM Pokemon
            ORDER BY pokemon_id
            """;
        using var reader = command.ExecuteReader();

        var pokemonById = new Dictionary<int, PokemonInfo>();
        while(reader.Read())
        {
            var pokemon = new PokemonInfo(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9));
            if (!pokemonById.TryAdd(pokemon.Id, pokemon))
                throw new InvalidDataException($"중복된 포켓몬 도감번호: {pokemon.Id}");
        }

        if (pokemonById.Count == 0)
            throw new InvalidDataException("포켓몬 DB가 비어 있습니다.");

        return pokemonById.ToFrozenDictionary();
    }

    private static FrozenDictionary<int, FrozenSet<int>> LoadLearnsets(
        SqliteConnection connection, FrozenDictionary<int, PokemonInfo> pokemon,
        FrozenDictionary<int, PokemonMoveInfo> moves)
    {
        var learnsets = pokemon.Keys.ToDictionary(id => id, _ => new HashSet<int>());
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT pokemon_id, move_id FROM PokemonLearnset";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            int pokemonId = reader.GetInt32(0);
            int moveId = reader.GetInt32(1);
            if (!learnsets.TryGetValue(pokemonId, out var ids) || !moves.ContainsKey(moveId))
                throw new InvalidDataException($"잘못된 습득 기술 연결: {pokemonId}, {moveId}");
            if (!ids.Add(moveId))
                throw new InvalidDataException($"중복된 습득 기술 연결: {pokemonId}, {moveId}");
        }
        if (learnsets.Values.Any(ids => ids.Count == 0))
            throw new InvalidDataException("포켓몬의 습득 기술 목록이 누락되었습니다.");
        return learnsets.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToFrozenSet());
    }
    private static FrozenDictionary<int, PokemonTypeInfo> LoadTypes(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT type_id, name, color_rgb FROM Type ORDER BY type_id";
        using var reader = command.ExecuteReader();
        var types = new Dictionary<int, PokemonTypeInfo>();
        while (reader.Read())
        {
            var type = new PokemonTypeInfo(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2));
            if (!types.TryAdd(type.Id, type))
                throw new InvalidDataException($"중복된 타입 ID: {type.Id}");
        }

        if (types.Count == 0)
            throw new InvalidDataException("타입 DB가 비어 있습니다.");
        return types.ToFrozenDictionary();
    }

    private static FrozenDictionary<int, PokemonMoveInfo> LoadMoves(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT move_id, name, type_id, category, power, accuracy, pp, priority
            FROM PokemonMove ORDER BY move_id
            """;
        using var reader = command.ExecuteReader();
        var moves = new Dictionary<int, PokemonMoveInfo>();
        while (reader.Read())
        {
            var move = new PokemonMoveInfo(
                reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7));
            if (!moves.TryAdd(move.Id, move))
                throw new InvalidDataException($"중복된 기술 ID: {move.Id}");
        }

        if (moves.Count == 0)
            throw new InvalidDataException("기술 DB가 비어 있습니다.");
        return moves.ToFrozenDictionary();
    }

    private static FrozenDictionary<(int AttackTypeId, int DefendTypeId), double>
        LoadEffectiveness(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT attack_type_id, defend_type_id, multiplier
            FROM TypeEffectiveness
            """;
        using var reader = command.ExecuteReader();
        var effectiveness = new Dictionary<(int AttackTypeId, int DefendTypeId), double>();
        while (reader.Read())
        {
            var pair = (reader.GetInt32(0), reader.GetInt32(1));
            if (!effectiveness.TryAdd(pair, reader.GetDouble(2)))
                throw new InvalidDataException($"중복된 타입 상성: {pair}");
        }

        return effectiveness.ToFrozenDictionary();
    }
}
