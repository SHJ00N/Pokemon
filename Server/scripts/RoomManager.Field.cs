// 날씨와 필드 효과. 수치는 6세대 규칙을 따른다. 기술로 만든 날씨·필드는 5턴 동안 이어진다.
partial class RoomManager
{
    private const string Rain = "rain", Sun = "sun", Sand = "sand", Hail = "hail";
    private const string GrassyTerrain = "grassy", MistyTerrain = "misty", ElectricTerrain = "electric";

    private const int GravityMoveId = 356, WonderRoomMoveId = 472, MudSportMoveId = 300, WaterSportMoveId = 346,
        IonDelugeMoveId = 569, FairyLockMoveId = 587;
    private const int WeatherBallMoveId = 311, SolarBeamMoveId = 76, ThunderMoveId = 87, HurricaneMoveId = 542,
        BlizzardMoveId = 59, GrowthMoveId = 74;
    // 그래스필드에서 땅에 있는 상대에게 위력이 절반인 기술: 지진, 땅고르기, 매그니튜드
    private static readonly HashSet<int> GroundShakeMoveIds = [89, 523, 222];
    // 중력 중에는 쓸 수 없는 기술: 공중날기, 뛰어오르기, 프리폴, 점프킥, 무릎차기, 튀어오르기, 전자부유, 텔레키네시스
    private static readonly HashSet<int> GravityBannedMoveIds = [19, 340, 507, 26, 136, 150, 393, 477];

    // 날씨 기술: 비바라기, 쾌청, 모래바람, 싸라기눈
    private static readonly Dictionary<int, string> WeatherMoves = new() { [240] = Rain, [241] = Sun, [201] = Sand, [258] = Hail };
    private static readonly Dictionary<string, (string Start, string Continue, string End)> WeatherText = new()
    {
        [Rain] = ("비가 내리기 시작했다!", "비가 계속 내리고 있다.", "비가 그쳤다!"),
        [Sun] = ("햇살이 강해졌다!", "햇살이 강하다.", "햇살이 원래대로 돌아왔다!"),
        [Sand] = ("모래바람이 불기 시작했다!", "모래바람이 세차게 분다.", "모래바람이 가라앉았다!"),
        [Hail] = ("싸라기눈이 내리기 시작했다!", "싸라기눈이 계속 내리고 있다.", "싸라기눈이 그쳤다!"),
    };
    // 필드 기술: 그래스필드, 미스트필드, 일렉트릭필드. 새 필드를 펼치면 전의 필드는 사라진다.
    private static readonly Dictionary<int, string> TerrainMoves = new()
    {
        [580] = GrassyTerrain, [581] = MistyTerrain, [604] = ElectricTerrain,
    };
    private static readonly Dictionary<string, (string Start, string End)> TerrainText = new()
    {
        [GrassyTerrain] = ("발밑에 풀이 무성해졌다!", "발밑의 풀이 사라졌다!"),
        [MistyTerrain] = ("발밑에 안개가 자욱해졌다!", "발밑의 안개가 사라졌다!"),
        [ElectricTerrain] = ("발밑에 전기가 흐르기 시작했다!", "발밑의 전기가 사라졌다!"),
    };

    private static readonly Dictionary<string, string> WeatherNames = new()
    {
        [Rain] = "비", [Sun] = "쾌청", [Sand] = "모래바람", [Hail] = "싸라기눈",
    };
    private static readonly Dictionary<string, string> TerrainNames = new()
    {
        [GrassyTerrain] = "그래스필드", [MistyTerrain] = "미스트필드", [ElectricTerrain] = "일렉트릭필드",
    };

    // 전투 화면 맨 윗줄에 표시할 날씨·필드·공간 효과와 남은 턴 (BattleView.FieldEffects)
    private static string[] GetFieldEffects(GameRoom room)
    {
        var effects = new List<string>();
        if (room.Weather is string weather)
            effects.Add($"날씨: {WeatherNames[weather]} ({room.WeatherTurns}턴)");
        if (room.Terrain is string terrain)
            effects.Add($"필드: {TerrainNames[terrain]} ({room.TerrainTurns}턴)");
        foreach (var (name, turns) in new[]
        {
            ("트릭룸", room.TrickRoomTurns), ("중력", room.GravityTurns), ("원더룸", room.WonderRoomTurns),
            ("흙놀이", room.MudSportTurns), ("물놀이", room.WaterSportTurns), ("페어리록", room.FairyLockTurns)
        })
        {
            if (turns > 0)
                effects.Add($"{name} ({turns}턴)");
        }
        return effects.ToArray();
    }

    // UseFieldMove가 처리하는 변화 기술
    private static readonly HashSet<int> FieldMoveIds =
    [
        .. WeatherMoves.Keys, .. TerrainMoves.Keys,
        GravityMoveId, WonderRoomMoveId, MudSportMoveId, WaterSportMoveId, IonDelugeMoveId, FairyLockMoveId
    ];

    private void UseFieldMove(GameRoom room, BattleSide side, PokemonMoveInfo move)
    {
        if (WeatherMoves.TryGetValue(move.Id, out string? weather))
        {
            if (room.Weather == weather)
            {
                LogBoth(room, side, "하지만 실패했다!");
                return;
            }
            room.Weather = weather;
            room.WeatherTurns = 5;
            LogBoth(room, side, WeatherText[weather].Start);
            return;
        }
        if (TerrainMoves.TryGetValue(move.Id, out string? terrain))
        {
            if (room.Terrain == terrain)
            {
                LogBoth(room, side, "하지만 실패했다!");
                return;
            }
            room.Terrain = terrain;
            room.TerrainTurns = 5;
            LogBoth(room, side, TerrainText[terrain].Start);
            return;
        }

        switch (move.Id)
        {
            case GravityMoveId:
                if (StartFieldEffect(room, side, room.GravityTurns, 5, value => room.GravityTurns = value, "중력이 강해졌다!"))
                    BringDownFromSky(room);
                break;
            case WonderRoomMoveId:
                // 원더룸 중에 다시 쓰면 원래대로 돌아온다.
                room.WonderRoomTurns = room.WonderRoomTurns > 0 ? 0 : 5;
                LogBoth(room, side, room.WonderRoomTurns > 0
                    ? "방어와 특수방어가 바뀌는 이상한 공간이 펼쳐졌다!" : "원더룸이 원래대로 돌아왔다!");
                break;
            case MudSportMoveId:
                StartFieldEffect(room, side, room.MudSportTurns, 5, value => room.MudSportTurns = value, "전기의 위력이 약해졌다!");
                break;
            case WaterSportMoveId:
                StartFieldEffect(room, side, room.WaterSportTurns, 5, value => room.WaterSportTurns = value, "불꽃의 위력이 약해졌다!");
                break;
            case IonDelugeMoveId:
                room.IonDeluge = true;
                LogBoth(room, side, "전자가 흩뿌려졌다!");
                break;
            case FairyLockMoveId:
                // 이번 턴 끝과 다음 턴 끝에 1씩 줄어서, 다음 턴에는 교체할 수 없다.
                StartFieldEffect(room, side, room.FairyLockTurns, 2, value => room.FairyLockTurns = value,
                    "다음 턴에는 교체할 수 없게 되었다!");
                break;
        }
    }

    // 이미 같은 효과가 있으면 실패한다. 성공하면 true
    private static bool StartFieldEffect(GameRoom room, BattleSide side, int turnsLeft, int duration,
        Action<int> setTurns, string message)
    {
        if (turnsLeft > 0)
        {
            LogBoth(room, side, "하지만 실패했다!");
            return false;
        }
        setTurns(duration);
        LogBoth(room, side, message);
        return true;
    }

    // 중력: 공중에 있는 포켓몬(공중날기·뛰어오르기·프리폴)을 떨어뜨린다. 모으던 기술은 취소된다.
    // 전자부유·텔레키네시스도 끝난다.
    private static void BringDownFromSky(GameRoom room)
    {
        foreach (BattleSide side in new[] { room.HostSide!, room.GuestSide! })
        {
            BattlePokemon pokemon = side.Current;
            bool floating = pokemon.MagnetRiseTurns > 0 || pokemon.TelekinesisTurns > 0;
            pokemon.MagnetRiseTurns = pokemon.TelekinesisTurns = 0;
            bool flying = pokemon.Hidden == Air && !pokemon.SkyDropped;
            if (flying)
                EndCharging(side, GetOther(room, side));
            if (flying || floating)
                LogPokemon(room, side, $"{WithJosa(pokemon.Info.Name, "은", "는")} 중력 때문에 떨어졌다!");
        }
    }

    // 땅에 있는지: 비행 타입이 아니면 땅에 있다. 날개쉬기를 쓴 턴, 뿌리박기 중, 중력 중에는 비행 타입도 땅에 있다.
    // 떨어뜨리기를 맞았으면 땅에 있고, 전자부유·텔레키네시스 중에는 땅에 없다.
    private bool IsGrounded(GameRoom room, BattlePokemon pokemon)
    {
        if (room.GravityTurns > 0 || pokemon.Ingrained || pokemon.SmackedDown)
            return true;
        if (pokemon.MagnetRiseTurns > 0 || pokemon.TelekinesisTurns > 0)
            return false;
        return pokemon.Roosting || !HasType(pokemon, "비행");
    }

    // 날씨·필드에 따라 기술의 타입·위력·명중률을 바꾼 기술 정보를 돌려준다.
    private PokemonMoveInfo AdjustMove(GameRoom room, BattlePokemon user, PokemonMoveInfo move)
    {
        // 웨더볼: 날씨가 있으면 위력 100, 날씨에 따라 불꽃·물·바위·얼음 타입
        if (move.Id == WeatherBallMoveId && room.Weather is string weather)
            move = move with { Power = 100, TypeId = TypeIdOf(weather switch { Sun => "불꽃", Rain => "물", Sand => "바위", _ => "얼음" }) };
        // 솔라빔: 비·모래바람·싸라기눈에서는 위력 절반
        if (move.Id == SolarBeamMoveId && room.Weather is Rain or Sand or Hail)
            move = move with { Power = move.Power / 2 };
        // 번개·폭풍: 비에서는 반드시 맞고 쾌청에서는 명중률 50. 눈보라: 싸라기눈에서는 반드시 맞는다.
        if (move.Id is ThunderMoveId or HurricaneMoveId && room.Weather is Rain or Sun)
            move = move with { Accuracy = room.Weather == Rain ? null : 50 };
        if (move.Id == BlizzardMoveId && room.Weather == Hail)
            move = move with { Accuracy = null };
        // 잠재파워: 개체값이 모두 31이라 악 타입, 위력 70 (6세대 계산식)
        if (move.Id == HiddenPowerMoveId)
            move = move with { TypeId = TypeIdOf("악"), Power = 70 };
        // 플라스마샤워: 이번 턴에 노말 타입 기술은 전기 타입이 된다.
        // 플라스마샤워는 노말 기술만, 송전은 그 포켓몬이 이번 턴에 쓰는 모든 기술을 전기 타입으로 바꾼다.
        if ((room.IonDeluge && HasTypeName(move.TypeId, "노말")) || user.Electrified)
            move = move with { TypeId = TypeIdOf("전기") };
        return move;
    }

    private int TypeIdOf(string name) => data.TypesById.Values.First(type => type.Name == name).Id;

    // 날씨·필드·흙놀이·물놀이에 따른 데미지 배율 (위력과 날씨 보정)
    private double FieldPowerModifier(GameRoom room, BattlePokemon attacker, BattlePokemon defender, PokemonMoveInfo move)
    {
        double modifier = 1;
        bool fire = HasTypeName(move.TypeId, "불꽃"), water = HasTypeName(move.TypeId, "물");
        // 쾌청: 불꽃 1.5배, 물 0.5배 / 비: 물 1.5배, 불꽃 0.5배
        if (room.Weather == Sun) modifier *= fire ? 1.5 : water ? 0.5 : 1;
        if (room.Weather == Rain) modifier *= water ? 1.5 : fire ? 0.5 : 1;
        // 필드: 땅에 있는 포켓몬이 쓰는 풀(그래스필드)·전기(일렉트릭필드) 기술 1.5배,
        // 땅에 있는 상대에게 쓰는 드래곤 기술(미스트필드)과 지진·땅고르기·매그니튜드(그래스필드) 0.5배
        if (room.Terrain == GrassyTerrain && HasTypeName(move.TypeId, "풀") && IsGrounded(room, attacker)) modifier *= 1.5;
        if (room.Terrain == ElectricTerrain && HasTypeName(move.TypeId, "전기") && IsGrounded(room, attacker)) modifier *= 1.5;
        if (room.Terrain == MistyTerrain && HasTypeName(move.TypeId, "드래곤") && IsGrounded(room, defender)) modifier *= 0.5;
        if (room.Terrain == GrassyTerrain && GroundShakeMoveIds.Contains(move.Id) && IsGrounded(room, defender)) modifier *= 0.5;
        // 흙놀이: 전기 기술 위력 1/3, 물놀이: 불꽃 기술 위력 1/3 (6세대)
        if (room.MudSportTurns > 0 && HasTypeName(move.TypeId, "전기")) modifier /= 3;
        if (room.WaterSportTurns > 0 && fire) modifier /= 3;
        return modifier;
    }

    // 쾌청에서는 얼음 상태가 되지 않는다. 미스트필드는 땅에 있는 포켓몬을 상태이상·혼란에서,
    // 일렉트릭필드는 잠듦(하품 포함)에서 지킨다. 막히면 그 이유를, 아니면 null을 돌려준다.
    private string? FieldBlocksStatus(GameRoom room, BattlePokemon target, string code)
    {
        bool grounded = IsGrounded(room, target) && target.Hidden == null;
        if (room.Terrain == MistyTerrain && grounded && code != Infatuation)
            return $"{WithJosa(target.Info.Name, "은", "는")} 미스트필드에 보호받고 있다!";
        if (room.Terrain == ElectricTerrain && grounded && code == Sleep)
            return $"{WithJosa(target.Info.Name, "은", "는")} 일렉트릭필드에 보호받고 있다!";
        if ((room.Weather == Sun && code == Freeze) || (code == Sleep && UproarActive(room)))
            return "";   // 쾌청에서는 얼지 않고, 소란피기 중에는 아무도 잠들지 않는다.
        return null;
    }

    // 턴 끝 처음: 날씨의 남은 턴을 줄이고, 계속되면 모래바람·싸라기눈 데미지를 준다.
    // 바위·땅·강철 타입은 모래바람, 얼음 타입은 싸라기눈에 데미지를 받지 않는다. 구멍파기·다이빙으로 숨은 포켓몬도 받지 않는다.
    private void TickWeather(GameRoom room, IEnumerable<BattleSide> sides)
    {
        if (room.Weather is not string weather)
            return;
        if (--room.WeatherTurns == 0)
        {
            room.Weather = null;
            LogBoth(room, room.HostSide!, WeatherText[weather].End);
            return;
        }
        LogBoth(room, room.HostSide!, WeatherText[weather].Continue);
        if (weather is not (Sand or Hail))
            return;
        foreach (BattleSide side in sides)
        {
            BattlePokemon pokemon = side.Current;
            bool immune = weather == Sand
                ? HasType(pokemon, "바위") || HasType(pokemon, "땅") || HasType(pokemon, "강철")
                : HasType(pokemon, "얼음");
            if (pokemon.Hp == 0 || immune || pokemon.Hidden is Underground or Underwater)
                continue;
            int damage = Math.Max(1, pokemon.MaxHp / 16);
            string cause = weather == Sand ? "모래바람에 의한 데미지를 입었다!" : "싸라기눈을 맞았다!";
            LogPokemon(room, side, $"{WithJosa(pokemon.Info.Name, "은", "는")} {cause} ({damage} 데미지)");
            DealDamage(room, side, damage);
            SendBattleState(room);
        }
    }

    // 그래스필드: 턴이 끝날 때 땅에 있는 포켓몬이 최대 HP의 1/16을 회복한다.
    private void GrassyTerrainHealing(GameRoom room, IEnumerable<BattleSide> sides)
    {
        if (room.Terrain != GrassyTerrain)
            return;
        foreach (BattleSide side in sides)
            if (IsGrounded(room, side.Current))
                Heal(room, side, side.Current.MaxHp / 16);
    }

    // 턴 끝 마지막: 필드·중력·원더룸·흙놀이·물놀이·페어리록의 남은 턴을 줄이고, 플라스마샤워를 끝낸다.
    private static void TickFields(GameRoom room)
    {
        BattleSide host = room.HostSide!;
        if (room.Terrain is string terrain && --room.TerrainTurns == 0)
        {
            room.Terrain = null;
            LogBoth(room, host, TerrainText[terrain].End);
        }
        if (room.GravityTurns > 0 && --room.GravityTurns == 0)
            LogBoth(room, host, "중력이 원래대로 돌아왔다!");
        if (room.WonderRoomTurns > 0 && --room.WonderRoomTurns == 0)
            LogBoth(room, host, "원더룸이 원래대로 돌아왔다!");
        if (room.MudSportTurns > 0 && --room.MudSportTurns == 0)
            LogBoth(room, host, "흙놀이의 효과가 사라졌다!");
        if (room.WaterSportTurns > 0 && --room.WaterSportTurns == 0)
            LogBoth(room, host, "물놀이의 효과가 사라졌다!");
        if (room.FairyLockTurns > 0)
            room.FairyLockTurns--;
        room.IonDeluge = false;
    }
}
