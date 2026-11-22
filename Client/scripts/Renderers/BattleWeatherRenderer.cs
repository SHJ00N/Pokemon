namespace Client;

// 배경을 먼저 그려서 포켓몬과 이펙트의 색상 및 윤곽을 유지한다.
static class BattleWeatherRenderer
{
    private static readonly BattleBackgroundAnimation sun = new("sun_weather_animation.txt");

    public static void Draw(string? weather, long now, int width, int top, int bottom, BattleSpriteBuffer buffer)
    {
        int rows = bottom - top;
        if (width <= 0 || rows <= 0) return;

        switch (weather)
        {
            case "rain":
                DrawPrecipitation(now, width, top, rows, buffer, rain: true);
                break;
            case "hail":
                DrawPrecipitation(now, width, top, rows, buffer, rain: false);
                break;
            case "sun":
                sun.Draw(now, width, top, rows, buffer);
                break;
            case "sand":
                DrawSand(now, width, top, rows, buffer);
                break;
        }
    }

    private static void DrawPrecipitation(long now, int width, int top, int rows, BattleSpriteBuffer buffer, bool rain)
    {
        const string rainCore = "\x1b[38;2;104;176;222m", rainTail = "\x1b[38;2;57;99;135m";
        const string hailCore = "\x1b[38;2;191;219;235m", hailTail = "\x1b[38;2;101;145;175m";
        long tick = now / (rain ? 66 : 88);
        int drops = Math.Max(1, width * rows / (rain ? 34 : 48));
        for (int i = 0; i < drops; i++)
        {
            uint seed = Mix((uint)i + 13);
            int period = rows + 4 + (int)(seed % 9);
            long age = tick + seed % (uint)period;
            int row = (int)(age % period) - 2;
            uint spawn = Mix(seed ^ (uint)(age / period));
            int drift = rain ? row + 2 : (row + 2) / 4;
            int x = (int)((spawn + (uint)drift) % (uint)width);
            if ((uint)row < (uint)rows)
            {
                char glyph = rain ? '╲' : (seed % 2 == 0 ? '°' : '·');
                buffer.SetPixel(x, top + row, rain ? rainCore : hailCore, glyph);
                if (rain && row > 0 && x > 0)
                    buffer.SetPixel(x - 1, top + row - 1, rainTail, '╲');
            }
            else if (row == rows && seed % 3 == 0)
                buffer.SetPixel(x, top + rows - 1, rain ? rainTail : hailTail, rain ? '_' : '·');
            else if (row == rows + 1 && !rain && rows > 1 && seed % 3 == 0)
                buffer.SetPixel(x, top + rows - 2, hailTail, '·');
        }
    }

    private static void DrawSand(long now, int width, int top, int rows, BattleSpriteBuffer buffer)
    {
        const string trail = "\x1b[38;2;138;94;55m", dust = "\x1b[38;2;173;126;75m", gust = "\x1b[38;2;200;153;91m";
        int tick = (int)(now / 59 % width);
        int grains = Math.Max(1, width * rows / 30);
        for (int i = 0; i < grains; i++)
        {
            uint seed = Mix((uint)i + 731);
            int speed = 2 + (int)(seed % 3);
            int x = ((int)(seed % (uint)width) + tick * speed) % width;
            int baseRow = (int)((seed >> 16) % (uint)rows);
            int row = (baseRow + rows + (x / 12 + (int)(seed % 3)) % 3 - 1) % rows;
            bool streak = seed % 4 == 0;
            buffer.SetPixel(x, top + row, streak ? gust : dust, streak ? '~' : '·');
            if (x > 1)
                buffer.SetPixel(x - 2, top + row, trail, streak ? '-' : '·');
            if (streak && x > 4)
                buffer.SetPixel(x - 5, top + row, trail, '·');
        }
    }

    // 이전 프레임을 저장하지 않아도 시간과 입자 번호로 같은 위치를 계산한다.
    private static uint Mix(uint seed)
    {
        seed ^= seed >> 16;
        seed = unchecked(seed * 0x7feb352d);
        seed ^= seed >> 15;
        seed = unchecked(seed * 0x846ca68b);
        return seed ^ (seed >> 16);
    }
}
