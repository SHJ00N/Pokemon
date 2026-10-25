using System.Text.Json;
using Pokemon.Protocol;

namespace Client;

sealed class InGameState : ClientState
{
    protected override string Menu => "\n[게임 중]";
    private PokemonCatalogResponse? catalog;
    private PokemonCatalogEntry? selectedPokemon;
    private readonly List<int> selectedMoves = new();
    private bool submitted;
    private BattleView? battle;

    public override void ShowMenu() => InGameRenderer.Draw(
        catalog, selectedPokemon, selectedMoves, submitted, battle, Notification);

    public override ClientState HandleServerMessage(string message)
    {
        if (message.StartsWith("POKEMON_CATALOG "))
        {
            catalog = JsonSerializer.Deserialize<PokemonCatalogResponse>(message[16..])
                ?? throw new InvalidDataException("포켓몬 목록을 해석할 수 없습니다.");
            Notification = null;
            return this;
        }
        if (message.StartsWith("BATTLE_READY "))
        {
            battle = JsonSerializer.Deserialize<BattleView>(message[13..])
                ?? throw new InvalidDataException("전투 화면 정보를 해석할 수 없습니다.");
            Notification = "양쪽 플레이어의 포켓몬 선택이 완료되었습니다.";
            return this;
        }
        if (message == "SELECTION_ACCEPTED")
        {
            SetServerMessage("선택 완료! 상대방 선택을 기다립니다.");
            return this;
        }
        if (message == "ROOM_CLOSED")
            return new LobbyState();
        if (message.StartsWith("RETURN_TO_ROOM "))
        {
            string[] parts = message.Split(' ', 3);
            if (parts.Length == 3 && int.TryParse(parts[1], out int roomId))
                return new InRoomState(roomId, parts[2]);
        }
        if (message.StartsWith("ERROR ") && submitted && battle == null)
        {
            submitted = false;
            selectedPokemon = null;
            selectedMoves.Clear();
        }
        return base.HandleServerMessage(message);
    }

    public override async Task<bool> HandleCommand(
        string input, StreamWriter writer, CancellationToken cancellationToken)
    {
        Notification = null;

        /*
        battle 상태에서 실제 전투 처리 예시 서버의 InGameState.cs 에서 서버의 수신 처리도 해주어야 함.
        if (battle != null)
        {
            if (!int.TryParse(input, out int slot) || slot is < 1 or > 4)
            {
                Notification = "기술 번호 1~4를 입력하세요.";
                return true;
            }

            await writer.WriteLineAsync($"USE_MOVE {slot}");
            Notification = "기술을 선택했습니다. 상대방의 선택을 기다립니다.";
            return true;
        }
        */

        if (battle != null)
            Notification = "현재는 두 포켓몬 표시까지 구현되어 있습니다.";
        else if (catalog == null)
            Notification = "서버에서 포켓몬 목록을 받는 중입니다.";
        else if (submitted)
            Notification = "상대방의 선택을 기다리는 중입니다.";
        else if (!int.TryParse(input, out int id))
            Notification = "목록에 표시된 번호를 입력하세요.";
        else if (selectedPokemon == null)
        {
            PokemonCatalogEntry? pokemon = catalog.Pokemon.FirstOrDefault(p => p.PokemonId == id);
            if (pokemon == null)
                Notification = "목록에 있는 포켓몬 도감번호를 입력하세요.";
            else if (pokemon.LearnableMoveIds.Length < 4)
                Notification = "서로 다른 기술 4개를 배울 수 있는 포켓몬을 선택하세요.";
            else
                selectedPokemon = pokemon;
        }
        else if (!selectedPokemon.LearnableMoveIds.Contains(id))
            Notification = "선택한 포켓몬이 배울 수 있는 기술 번호를 입력하세요.";
        else if (selectedMoves.Contains(id))
            Notification = "이미 선택한 기술입니다. 다른 기술을 선택하세요.";
        else
        {
            selectedMoves.Add(id);
            if (selectedMoves.Count == 4)
            {
                var selection = new PokemonSelection(selectedPokemon.PokemonId, selectedMoves.ToArray());
                await writer.WriteLineAsync("SELECT_POKEMON " + JsonSerializer.Serialize(selection));
                submitted = true;
            }
        }
        return true;
    }
}
