namespace Client;

static class BattleTerrainRenderer
{
    private static readonly BattleBackgroundAnimation grassy = new("grassy_terrain_animation.txt");
    private static readonly BattleBackgroundAnimation misty = new("misty_terrain_animation.txt");
    private static readonly BattleBackgroundAnimation electric = new("electric_terrain_animation.txt");

    public static void Draw(string? terrain, long now, int width, int top, int bottom, BattleSpriteBuffer buffer)
    {
        var animation = terrain switch
        {
            "grassy" => grassy,
            "misty" => misty,
            "electric" => electric,
            _ => null
        };
        animation?.Draw(now, width, top, bottom - top, buffer);
    }
}
