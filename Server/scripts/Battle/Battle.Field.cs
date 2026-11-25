// 날씨와 필드 효과. 수치는 6세대 규칙을 따른다. 기술로 만든 날씨·필드는 5턴 동안 이어진다.
partial class Battle
{
    private const string Rain = "rain", Sun = "sun", Sand = "sand", Hail = "hail";
    private const string GrassyTerrain = "grassy", MistyTerrain = "misty", ElectricTerrain = "electric";

    private const int GravityMoveId = 356, TrickRoomMoveId = 433, WonderRoomMoveId = 472, MudSportMoveId = 300, WaterSportMoveId = 346,
        IonDelugeMoveId = 569, FairyLockMoveId = 587;
    private const int WeatherBallMoveId = 311, SolarBeamMoveId = 76, ThunderMoveId = 87, HurricaneMoveId = 542,
        BlizzardMoveId = 59, GrowthMoveId = 74;
    // 그래스필드에서 땅에 있는 상대에게 위력이 절반인 기술: 지진, 땅고르기, 매그니튜드
    private static readonly HashSet<int> GroundShakeMoveIds = [89, 523, 222];
    private bool IsBlockedByGravity(PokemonMoveInfo move) =>
        GravityTurns > 0 && HasFlag(move, "gravity");

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
    private string[] GetFieldEffects()
    {
        var effects = new List<string>();
        if (Weather is string weather)
            effects.Add($"날씨: {WeatherNames[weather]} ({WeatherTurns}턴)");
        if (Terrain is string terrain)
            effects.Add($"필드: {TerrainNames[terrain]} ({TerrainTurns}턴)");
        foreach (var (name, turns) in new[]
        {
            ("트릭룸", TrickRoomTurns), ("중력", GravityTurns), ("원더룸", WonderRoomTurns),
            ("흙놀이", MudSportTurns), ("물놀이", WaterSportTurns), ("페어리록", FairyLockTurns)
        })
        {
            if (turns > 0)
                effects.Add($"{name} ({turns}턴)");
        }
        return effects.ToArray();
    }

    // 정보 상자 아래에 표시할 한쪽 편의 효과 (BattleView.SideEffects·OpponentSideEffects).
    // 남은 턴이 있는 효과와, 이 편에 깔려 있어 교체해 나오는 포켓몬이 받는 설치기
    private string[] GetSideEffects(BattleSide side)
    {
        var effects = new List<string>();
        foreach (var (name, turns) in new[]
        {
            ("리플렉터", side.ReflectTurns), ("빛의장막", side.LightScreenTurns), ("신비의부적", side.SafeguardTurns),
            ("흰안개", side.MistTurns), ("순풍", side.TailwindTurns), ("주술", side.LuckyChantTurns)
        })
        {
            if (turns > 0)
                effects.Add($"{name} ({turns}턴)");
        }
        if (side.StealthRock) effects.Add("스텔스록");
        if (side.Spikes > 0) effects.Add($"압정뿌리기 {side.Spikes}층");
        if (side.ToxicSpikes > 0) effects.Add($"독압정 {side.ToxicSpikes}층");
        if (side.StickyWeb) effects.Add("끈적끈적네트");
        return effects.ToArray();
    }

    // UseFieldMove가 처리하는 변화 기술
    private static readonly HashSet<int> FieldMoveIds =
    [
        .. WeatherMoves.Keys, .. TerrainMoves.Keys,
        GravityMoveId, TrickRoomMoveId, WonderRoomMoveId, MudSportMoveId, WaterSportMoveId, IonDelugeMoveId, FairyLockMoveId
    ];

    private bool UseFieldMove(BattleSide side, PokemonMoveInfo move)
    {
        if (WeatherMoves.TryGetValue(move.Id, out string? weather))
        {
            if (Weather == weather)
            {
                LogBoth(side, "하지만 실패했다!");
                return false;
            }
            Weather = weather;
            WeatherTurns = 5;
            LogBoth(side, WeatherText[weather].Start);
            return true;
        }
        if (TerrainMoves.TryGetValue(move.Id, out string? terrain))
        {
            if (Terrain == terrain)
            {
                LogBoth(side, "하지만 실패했다!");
                return false;
            }
            Terrain = terrain;
            TerrainTurns = 5;
            LogBoth(side, TerrainText[terrain].Start);
            return true;
        }

        switch (move.Id)
        {
            case GravityMoveId:
                if (!StartFieldEffect(side, GravityTurns, 5, value => GravityTurns = value, "중력이 강해졌다!"))
                    return false;
                BringDownFromSky();
                return true;
            case TrickRoomMoveId:
                // 트릭룸 중에 다시 쓰면 원래대로 돌아온다.
                TrickRoomTurns = TrickRoomTurns > 0 ? 0 : 5;
                LogBoth(side, TrickRoomTurns > 0 ? "시공이 뒤틀렸다!" : "뒤틀린 시공이 원래대로 돌아왔다!");
                return true;
            case WonderRoomMoveId:
                // 원더룸 중에 다시 쓰면 원래대로 돌아온다.
                WonderRoomTurns = WonderRoomTurns > 0 ? 0 : 5;
                LogBoth(side, WonderRoomTurns > 0
                    ? "방어와 특수방어가 바뀌는 이상한 공간이 펼쳐졌다!" : "원더룸이 원래대로 돌아왔다!");
                return true;
            case MudSportMoveId:
                return StartFieldEffect(side, MudSportTurns, 5, value => MudSportTurns = value, "전기의 위력이 약해졌다!");
            case WaterSportMoveId:
                return StartFieldEffect(side, WaterSportTurns, 5, value => WaterSportTurns = value, "불꽃의 위력이 약해졌다!");
            case IonDelugeMoveId:
                IonDeluge = true;
                LogBoth(side, "전자가 흩뿌려졌다!");
                return true;
            case FairyLockMoveId:
                // 이번 턴 끝과 다음 턴 끝에 1씩 줄어서, 다음 턴에는 교체할 수 없다.
                return StartFieldEffect(side, FairyLockTurns, 2, value => FairyLockTurns = value,
                    "다음 턴에는 교체할 수 없게 되었다!");
            default:
                return false;
        }
    }

    // 이미 같은 효과가 있으면 실패한다. 성공하면 true
    private bool StartFieldEffect(BattleSide side, int turnsLeft, int duration,
        Action<int> setTurns, string message)
    {
        if (turnsLeft > 0)
        {
            LogBoth(side, "하지만 실패했다!");
            return false;
        }
        setTurns(duration);
        LogBoth(side, message);
        return true;
    }

    // 중력: 공중에 있는 포켓몬(공중날기·뛰어오르기·프리폴)을 떨어뜨린다. 모으던 기술은 취소된다.
    // 전자부유·텔레키네시스도 끝난다.
    private void BringDownFromSky()
    {
        foreach (BattleSide side in new[] { HostSide!, GuestSide! })
        {
            BattlePokemon pokemon = side.Current;
            bool floating = pokemon.MagnetRiseTurns > 0 || pokemon.TelekinesisTurns > 0;
            pokemon.MagnetRiseTurns = pokemon.TelekinesisTurns = 0;
            bool flying = pokemon.Hidden == Air && !pokemon.SkyDropped;
            if (flying)
            {
                EndCharging(side, GetOther(side));
                TurnQueue.Remove(side);
            }
            if (flying || floating)
                LogPokemon(side, $"{WithJosa(pokemon.Info.Name, "은", "는")} 중력 때문에 떨어졌다!");
        }
    }

    // 땅에 있는지: 비행 타입이 아니면 땅에 있다. 날개쉬기를 쓴 턴, 뿌리박기 중, 중력 중에는 비행 타입도 땅에 있다.
    // 떨어뜨리기를 맞았으면 땅에 있고, 전자부유·텔레키네시스 중에는 땅에 없다.
    private bool IsGrounded(BattlePokemon pokemon)
    {
        if (GravityTurns > 0 || pokemon.Ingrained || pokemon.SmackedDown)
            return true;
        if (pokemon.MagnetRiseTurns > 0 || pokemon.TelekinesisTurns > 0)
            return false;
        return pokemon.Roosting || !HasType(pokemon, "비행");
    }

    // 날씨·필드에 따라 기술의 타입·위력·명중률을 바꾼 기술 정보를 돌려준다.
    private PokemonMoveInfo AdjustMove(BattlePokemon user, PokemonMoveInfo move)
    {
        // 웨더볼: 날씨가 있으면 위력 100, 날씨에 따라 불꽃·물·바위·얼음 타입
        if (move.Id == WeatherBallMoveId && Weather is string weather)
            move = move with { Power = 100, TypeId = TypeIdOf(weather switch { Sun => "불꽃", Rain => "물", Sand => "바위", _ => "얼음" }) };
        // 솔라빔: 비·모래바람·싸라기눈에서는 위력 절반
        if (move.Id == SolarBeamMoveId && Weather is Rain or Sand or Hail)
            move = move with { Power = move.Power / 2 };
        // 번개·폭풍: 비에서는 반드시 맞고 쾌청에서는 명중률 50. 눈보라: 싸라기눈에서는 반드시 맞는다.
        if (move.Id is ThunderMoveId or HurricaneMoveId && Weather is Rain or Sun)
            move = move with { Accuracy = Weather == Rain ? null : 50 };
        if (move.Id == BlizzardMoveId && Weather == Hail)
            move = move with { Accuracy = null };
        // 잠재파워: 개체값이 모두 31이라 악 타입, 위력 70 (6세대 계산식)
        if (move.Id == HiddenPowerMoveId)
            move = move with { TypeId = TypeIdOf("악"), Power = 70 };
        // 플라스마샤워: 이번 턴에 노말 타입 기술은 전기 타입이 된다.
        // 플라스마샤워는 노말 기술만, 송전은 그 포켓몬이 이번 턴에 쓰는 모든 기술을 전기 타입으로 바꾼다.
        if ((IonDeluge && HasTypeName(move.TypeId, "노말")) || user.Electrified)
            move = move with { TypeId = TypeIdOf("전기") };
        return move;
    }

    private int TypeIdOf(string name) => data.TypesById.Values.First(type => type.Name == name).Id;

    // 날씨·필드·흙놀이·물놀이에 따른 데미지 배율 (위력과 날씨 보정)
    private double FieldPowerModifier(BattlePokemon attacker, BattlePokemon defender, PokemonMoveInfo move)
    {
        double modifier = 1;
        bool fire = HasTypeName(move.TypeId, "불꽃"), water = HasTypeName(move.TypeId, "물");
        // 쾌청: 불꽃 1.5배, 물 0.5배 / 비: 물 1.5배, 불꽃 0.5배
        if (Weather == Sun) modifier *= fire ? 1.5 : water ? 0.5 : 1;
        if (Weather == Rain) modifier *= water ? 1.5 : fire ? 0.5 : 1;
        // 필드: 땅에 있는 포켓몬이 쓰는 풀(그래스필드)·전기(일렉트릭필드) 기술 1.5배,
        // 땅에 있는 상대에게 쓰는 드래곤 기술(미스트필드)과 지진·땅고르기·매그니튜드(그래스필드) 0.5배
        if (Terrain == GrassyTerrain && HasTypeName(move.TypeId, "풀") && IsGrounded(attacker)) modifier *= 1.5;
        if (Terrain == ElectricTerrain && HasTypeName(move.TypeId, "전기") && IsGrounded(attacker)) modifier *= 1.5;
        if (Terrain == MistyTerrain && HasTypeName(move.TypeId, "드래곤") && IsGrounded(defender)) modifier *= 0.5;
        if (Terrain == GrassyTerrain && GroundShakeMoveIds.Contains(move.Id) && IsGrounded(defender)) modifier *= 0.5;
        // 흙놀이: 전기 기술 위력 1/3, 물놀이: 불꽃 기술 위력 1/3 (6세대)
        if (MudSportTurns > 0 && HasTypeName(move.TypeId, "전기")) modifier /= 3;
        if (WaterSportTurns > 0 && fire) modifier /= 3;
        return modifier;
    }

    // 쾌청에서는 얼음 상태가 되지 않는다. 미스트필드는 땅에 있는 포켓몬을 상태이상·혼란에서,
    // 일렉트릭필드는 잠듦(하품 포함)에서 지킨다. 막히면 그 이유를, 아니면 null을 돌려준다.
    private string? FieldBlocksStatus(BattlePokemon target, string code)
    {
        bool grounded = IsGrounded(target) && target.Hidden == null;
        if (Terrain == MistyTerrain && grounded && code != Infatuation)
            return $"{WithJosa(target.Info.Name, "은", "는")} 미스트필드에 보호받고 있다!";
        if (Terrain == ElectricTerrain && grounded && code == Sleep)
            return $"{WithJosa(target.Info.Name, "은", "는")} 일렉트릭필드에 보호받고 있다!";
        if ((Weather == Sun && code == Freeze) || (code == Sleep && UproarActive()))
            return "";   // 쾌청에서는 얼지 않고, 소란피기 중에는 아무도 잠들지 않는다.
        return null;
    }

    // 턴 끝 처음: 날씨의 남은 턴을 줄이고, 계속되면 모래바람·싸라기눈 데미지를 준다.
    // 바위·땅·강철 타입은 모래바람, 얼음 타입은 싸라기눈에 데미지를 받지 않는다. 구멍파기·다이빙으로 숨은 포켓몬도 받지 않는다.
    private void TickWeather(IEnumerable<BattleSide> sides)
    {
        if (Weather is not string weather)
            return;
        if (--WeatherTurns == 0)
        {
            Weather = null;
            LogBoth(HostSide!, WeatherText[weather].End);
            return;
        }
        LogBoth(HostSide!, WeatherText[weather].Continue);
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
            LogPokemon(side, $"{WithJosa(pokemon.Info.Name, "은", "는")} {cause} ({damage} 데미지)");
            DealDamage(side, damage);
            SendBattleState();
        }
    }

    // 그래스필드: 턴이 끝날 때 땅에 있는 포켓몬이 최대 HP의 1/16을 회복한다.
    private void GrassyTerrainHealing(IEnumerable<BattleSide> sides)
    {
        if (Terrain != GrassyTerrain)
            return;
        foreach (BattleSide side in sides)
            if (IsGrounded(side.Current) && Heal(side, side.Current.MaxHp / 16, "그래스필드"))
                SendBattleState();
    }

    // 턴 끝 마지막: 필드·중력·원더룸·흙놀이·물놀이·페어리록의 남은 턴을 줄이고, 플라스마샤워를 끝낸다.
    private void TickFields()
    {
        BattleSide host = HostSide!;
        if (Terrain is string terrain && --TerrainTurns == 0)
        {
            Terrain = null;
            LogBoth(host, TerrainText[terrain].End);
        }
        if (GravityTurns > 0 && --GravityTurns == 0)
            LogBoth(host, "중력이 원래대로 돌아왔다!");
        if (WonderRoomTurns > 0 && --WonderRoomTurns == 0)
            LogBoth(host, "원더룸이 원래대로 돌아왔다!");
        if (MudSportTurns > 0 && --MudSportTurns == 0)
            LogBoth(host, "흙놀이의 효과가 사라졌다!");
        if (WaterSportTurns > 0 && --WaterSportTurns == 0)
            LogBoth(host, "물놀이의 효과가 사라졌다!");
        if (FairyLockTurns > 0)
            FairyLockTurns--;
        IonDeluge = false;
    }
}
