using System.Text.Json;
using Pokemon.Protocol;

// 로비: 닉네임, 방 목록, 방 만들기·입장·준비·나가기, 포켓몬 선택.
// 1:1 대전 진행은 Single/RoomManager.Single.cs, 전투 규칙은 Battle/Battle*.cs에 있다.
partial class RoomManager
{
    private const int TeamSize = 3;

    private const int MaxNicknameLength = 8;

    private const int MaxSelectableSpeciesId = 905;

    // 지금은 팀에 넣을 수 없는 종: 루브도(스케치는 2대2 대전에서 쓸모가 있어 그때 연다), 안농. 목록에도 보내지 않는다.
    private static readonly HashSet<int> BlockedSpeciesIds = [235, 201];

    private readonly GameData data;

    private readonly string catalogMessage;     // POKEMON_CATALOG <Brotli로 압축한 JSON의 Base64>
    public int CatalogJsonBytes { get; }        // 압축 전 JSON 크기
    public int CatalogMessageBytes => catalogMessage.Length + 1;   // 실제로 보내는 크기 (ASCII, 줄바꿈 포함)
    private readonly object roomsLock = new();

    private readonly Dictionary<int, GameRoom> rooms = new();

    private readonly HashSet<string> nicknames = new();     // 접속 중인 플레이어의 닉네임 (중복 금지)
    private int nextRoomId = 1000;

    private readonly int? seed;                     // 있으면 방마다 new Random(seed + 방 번호)
    private readonly int actionTimeLimitSeconds;    // 턴마다 행동 선택 제한시간. 테스트에서만 기본값(60초)을 바꾼다.

    public RoomManager(GameData data, int? seed = null, int actionTimeLimitSeconds = ActionTimeLimitSeconds)
    {
        this.data = data;
        this.seed = seed;
        this.actionTimeLimitSeconds = actionTimeLimitSeconds;
        // 전국도감 905번까지 팀에 넣을 수 있는 폼만 보낸다. 폼 번호가 아닌 종 번호로 제한한다.
        var selectable = data.PokemonById.Values
            .Where(IsTeamSelectable)
            .OrderBy(p => p.SpeciesId).ThenBy(p => p.Id).ToArray();
        var formCount = selectable.CountBy(p => p.SpeciesId).ToDictionary();
        var pokemon = selectable
            .Select(p => new PokemonCatalogEntry(p.Id, p.SpeciesId, p.Name,
                formCount[p.SpeciesId] > 1 || !p.IsDefault ? p.FormName : null, Battle.GetTypes(data, p),
                data.LearnableMovesByPokemonId[p.Id].Order().ToArray(), p.Generation,
                data.MegaFormsByBaseId.ContainsKey(p.Id))).ToArray();
        // 기술은 고를 수 있는 폼이 배우는 것만 보낸다 (Z기술·다이맥스기 등은 빠진다).
        var learnable = pokemon.SelectMany(p => p.LearnableMoveIds).ToHashSet();
        var moves = data.MovesById.Values.Where(m => learnable.Contains(m.Id)).OrderBy(m => m.Id).Select(m => Battle.GetMove(data, m)).ToArray();
        // 게임마다 같은 목록을 보내므로 서버 시작 시 한 번만 압축해 둔다.
        catalogMessage = "POKEMON_CATALOG " + CatalogCodec.Encode(new PokemonCatalogResponse(pokemon, moves), out int jsonBytes);
        CatalogJsonBytes = jsonBytes;
    }

    // 접속 직후 닉네임을 정하고 로비로 보낸다. 닉네임은 띄어쓰기 없이 1~8글자이며 접속 중인 다른 플레이어와 겹치면 안 된다.
    // 메시지를 띄어쓰기로 나누므로 닉네임에 띄어쓰기를 허용하지 않는다.
    public void SetNickname(ClientSession session, string nickname)
    {
        lock (roomsLock)
        {
            if (session.State is not NicknameState)
            {
                session.QueueMessage("ERROR 이미 닉네임을 정했습니다.");
                return;
            }
            if (nickname.Length is 0 or > MaxNicknameLength ||
                nickname.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            {
                session.QueueMessage($"ERROR 닉네임은 띄어쓰기 없이 1~{MaxNicknameLength}글자로 입력하세요.");
                return;
            }
            if (!nicknames.Add(nickname))
            {
                session.QueueMessage("ERROR 이미 사용 중인 닉네임입니다.");
                return;
            }

            session.Nickname = nickname;
            session.State = new LobbyState();
            session.QueueMessage($"NAME_ACCEPTED {nickname}");
            Console.WriteLine($"{session.Address}: 닉네임 {nickname}");
        }
    }

    // 연결이 끊기면 방에서 내보내고 닉네임을 다른 플레이어가 쓸 수 있게 돌려준다.
    public void Disconnect(ClientSession session)
    {
        lock (roomsLock)
        {
            LeaveRoom(session);
            if (session.Nickname.Length > 0)
                nicknames.Remove(session.Nickname);
        }
    }

    // 로비의 방 목록. 게임 중이거나 가득 찬 방도 상태와 함께 보낸다.
    public void SendRoomList(ClientSession session)
    {
        lock (roomsLock)
        {
            RoomSummary[] list = rooms.Values.OrderBy(room => room.Id)
                .Select(room => new RoomSummary(room.Id, room.Host.Nickname, room.Guest == null ? 1 : 2,
                    room.Host.State is not InRoomState))
                .ToArray();
            session.QueueMessage("ROOM_LIST " + JsonSerializer.Serialize(list));
        }
    }

    public void CreateRoom(ClientSession session)
    {
        lock (roomsLock)
        {
            if (session.RoomId != null)
            {
                session.QueueMessage("ERROR 이미 다른 방에 참여 중입니다.");
                return;
            }

            int roomId = nextRoomId++;
            var room = new GameRoom(roomId, session, seed is int s ? new Random(s + roomId) : new Random());
            rooms.Add(roomId, room);
            session.RoomId = roomId;
            session.State = new InRoomState();
            session.QueueMessage($"CREATED {roomId} {session.Nickname}");
            BroadcastPlayers(room);
            Console.WriteLine($"방 {roomId} 생성: {session.Nickname} ({session.Address})");
        }
    }

    public void JoinRoom(ClientSession session, int roomId)
    {
        lock (roomsLock)
        {
            if (session.RoomId != null)
            {
                session.QueueMessage("ERROR 이미 다른 방에 참여 중입니다.");
                return;
            }
            if (!rooms.TryGetValue(roomId, out GameRoom? room))
            {
                session.QueueMessage("ERROR 존재하지 않는 방입니다.");
                return;
            }
            if (room.Guest != null)
            {
                session.QueueMessage("ERROR 방이 가득 찼습니다.");
                return;
            }
            if (room.Host.State is not InRoomState)
            {
                session.QueueMessage("ERROR 이미 게임이 시작된 방입니다.");
                return;
            }

            room.Guest = session;
            session.RoomId = roomId;
            session.State = new InRoomState();
            session.QueueMessage($"JOINED {roomId} {room.Host.Nickname}");
            room.Host.QueueMessage("PLAYER_JOINED");
            BroadcastPlayers(room);
            Console.WriteLine($"방 {roomId} 입장: {session.Nickname} ({session.Address})");
        }
    }

    public void SetReady(ClientSession session)
    {
        lock (roomsLock)
        {
            if (session.RoomId is not int roomId || !rooms.TryGetValue(roomId, out GameRoom? room))
            {
                session.QueueMessage("ERROR 방에 참여 중이 아닙니다.");
                return;
            }

            if (session.State is not InRoomState)
            {
                session.QueueMessage("ERROR 현재 상태에서는 준비할 수 없습니다.");
                return;
            }

            bool wasReady = session.IsReady;
            session.IsReady = true;
            session.QueueMessage("READY");
            if (!wasReady)
            {
                ClientSession? opponent = room.Host.Id == session.Id ? room.Guest : room.Host;
                opponent?.QueueMessage("PLAYER_READY");
            }
            BroadcastPlayers(room);

            if (room.Guest is { IsReady: true } guest && room.Host.IsReady &&
                room.Host.State is InRoomState && guest.State is InRoomState)
            {
                room.Host.State = new InGameState();
                guest.State = new InGameState();
                room.Host.QueueMessage("GAME_STARTED");
                guest.QueueMessage("GAME_STARTED");
                room.Host.QueueMessage(catalogMessage);
                guest.QueueMessage(catalogMessage);
                room.TeamDeadline = Deadline(TeamTimeLimitSeconds);
            }
        }
    }

    // 서로 다른 포켓몬 3마리와 각각의 기술 최대 4개. 첫 번째 포켓몬이 먼저 출전한다.
    public void SelectTeam(ClientSession session, PokemonSelection[]? team)
    {
        lock (roomsLock)
        {
            if (session.State is not InGameState || session.RoomId is not int roomId ||
                !rooms.TryGetValue(roomId, out GameRoom? room) || room.Guest == null || room.BattleOver)
            {
                session.QueueMessage("ERROR 포켓몬을 선택할 수 있는 게임이 없습니다.");
                return;
            }

            bool isHost = room.Host.Id == session.Id;
            if ((isHost ? room.HostTeam : room.GuestTeam) != null)
            {
                session.QueueMessage("ERROR 이미 선택을 완료했습니다.");
                return;
            }
            // 같은 종은 폼이 달라도 한 마리만 넣을 수 있다 (예: 라이츄와 알로라 라이츄).
            if (team is not { Length: TeamSize } || team.Any(p => p == null || !IsValidSelection(p)) ||
                team.Select(p => data.PokemonById[p.PokemonId].SpeciesId).Distinct().Count() != TeamSize)
            {
                session.QueueMessage("ERROR 서로 다른 포켓몬 3마리와 각각 배울 수 있는 서로 다른 기술 4개를 고르세요. 4개 미만이면 모든 기술을 선택하세요.");
                return;
            }

            if (isHost) room.HostTeam = team;
            else room.GuestTeam = team;
            session.QueueMessage("SELECTION_ACCEPTED");

            if (room.HostTeam is { } hostTeam && room.GuestTeam is { } guestTeam)
                StartBattle(room, hostTeam, guestTeam);
        }
    }

    private static bool IsTeamSelectable(PokemonInfo info) =>
        info.IsSelectable && info.SpeciesId <= MaxSelectableSpeciesId && !BlockedSpeciesIds.Contains(info.SpeciesId);

    private bool IsValidSelection(PokemonSelection selection) =>
        data.PokemonById.TryGetValue(selection.PokemonId, out PokemonInfo? info) && IsTeamSelectable(info) &&
        selection.MoveIds is { Length: > 0 } ids &&
        ids.Length == PokemonSelection.RequiredMoveCount(data.LearnableMovesByPokemonId[selection.PokemonId].Count) &&
        ids.Distinct().Count() == ids.Length &&
        ids.All(id => data.LearnableMovesByPokemonId[selection.PokemonId].Contains(id));

    private BattleSide CreateSide(ClientSession player, PokemonSelection[] team) => new(player, team
        .Select(p => new BattlePokemon(data.PokemonById[p.PokemonId], p.MoveIds.Select(id => data.MovesById[id]).ToArray()))
        .ToArray());

    public void LeaveRoom(ClientSession session, bool notify = false)
    {
        lock (roomsLock)
        {
            if (session.RoomId is not int roomId)
            {
                if (notify)
                    session.QueueMessage("ERROR 방에 참여 중이 아닙니다.");
                return;
            }

            bool wasInGame = session.State is InGameState;
            session.RoomId = null;
            session.IsReady = false;
            session.State = new LobbyState();
            if (!rooms.TryGetValue(roomId, out GameRoom? room))
                return;

            ClientSession? opponent = room.Host.Id == session.Id ? room.Guest : room.Host;
            if (wasInGame && (room.Battle != null || room.BattleOver) && opponent != null)
                LeaveBattle(room, opponent);
            else if (room.Host.Id == session.Id)
            {
                rooms.Remove(roomId);
                if (room.Guest != null)
                {
                    room.Guest.RoomId = null;
                    room.Guest.IsReady = false;
                    room.Guest.State = new LobbyState();
                    room.Guest.QueueMessage("ROOM_CLOSED");
                }
                Console.WriteLine($"방 {roomId} 종료");
            }
            else if (room.Guest?.Id == session.Id)
            {
                room.Guest = null;
                room.Host.QueueMessage("PLAYER_LEFT");
                if (room.Host.State is InGameState)
                {
                    room.Host.State = new InRoomState();
                    room.Host.IsReady = false;
                    room.ResetBattle();
                    room.Host.QueueMessage($"RETURN_TO_ROOM {room.Id} {room.Host.Nickname}");
                }
                BroadcastPlayers(room);
                Console.WriteLine($"방 {roomId} 참여자 퇴장");
            }

            if (notify)
                session.QueueMessage("LEFT");
        }
    }

    // roomsLock 안에서 호출한다. 전투가 시작됐거나 결과가 난 뒤 한 명이 나가면(연결 종료 포함) 처리한다.
    // 전투 중이었다면 항복으로 처리한다.
    // 남은 플레이어는 방장이 되어 결과 화면에 남고, 나가기를 고르면 방이 닫힌다.
    private static void LeaveBattle(GameRoom room, ClientSession remaining)
    {
        room.Host = remaining;
        room.Guest = null;
        if (!room.BattleOver)
        {
            room.BattleOver = true;
            remaining.QueueMessage("BATTLE_END WIN LEFT");
            Console.WriteLine($"방 {room.Id} 전투 종료 (상대 퇴장)");
        }
        else
            remaining.QueueMessage("PLAYER_LEFT");   // 결과 화면에서 나감. 재도전은 할 수 없다.
        Console.WriteLine($"방 {room.Id} 퇴장, 남은 플레이어 {remaining.Nickname}");
    }

    // roomsLock 안에서 호출해 플레이어 목록과 준비 상태를 함께 전달한다.
    private static void BroadcastPlayers(GameRoom room)
    {
        string message = $"ROOM_PLAYERS {room.Id} {room.Host.Nickname} {(room.Host.IsReady ? 1 : 0)}";
        if (room.Guest is ClientSession guest)
            message += $" {guest.Nickname} {(guest.IsReady ? 1 : 0)}";

        room.Host.QueueMessage(message);
        room.Guest?.QueueMessage(message);
    }
}
