using System;
using System.Text;

class Program
{
    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // db 조회
        string databasePath = Path.Combine(AppContext.BaseDirectory, "resources\\db", "Pokemon.db");
        GameData data = GameData.Load(databasePath);
        Console.WriteLine(
            $"Loaded {data.PokemonById.Count} Pokemon, {data.TypesById.Count} types, " +
            $"{data.EffectivenessByType.Count} type matchups, and {data.MovesById.Count} moves, " +
            $"{data.LearnableMovesByPokemonId.Values.Sum(ids => ids.Count)} learnset entries from {databasePath}");
        
        int port = args.Length == 0 ? 5000 : int.Parse(args[0]);
    }
}