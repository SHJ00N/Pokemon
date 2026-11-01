using System.Text.Json;
using Pokemon.Protocol;

sealed class InGameState : ClientState
{
    public override bool HandleCommand(ClientSession session, RoomManager rooms, string command)
    {
        // 선공을 정하는 가위바위보 (1: 가위, 2: 바위, 3: 보)
        if (command.StartsWith("RPS "))
        {
            if (!int.TryParse(command[4..], out int choice) || choice is < 1 or > 3)
                session.QueueMessage("ERROR 가위바위보는 1~3 중에서 선택하세요.");
            else
                rooms.SubmitRps(session, choice);
            return true;
        }

        // 전투 중 기술 사용. 슬롯은 선택한 기술 4개 중 몇 번째인지를 뜻한다.
        if (command.StartsWith("USE_MOVE "))
        {
            if (!int.TryParse(command[9..], out int slot) || slot is < 1 or > 4)
                session.QueueMessage("ERROR 기술 번호는 1~4입니다.");
            else
                rooms.UseMove(session, slot);
            return true;
        }

        if (command.StartsWith("SELECT_POKEMON "))
        {
            try
            {
                // 포켓몬 및 기술 선택
                PokemonSelection? selection = JsonSerializer.Deserialize<PokemonSelection>(command[15..]);
                rooms.SelectPokemon(session, selection);
            }
            catch (JsonException)
            {
                session.QueueMessage("ERROR 포켓몬 선택 요청 형식이 올바르지 않습니다.");
            }

            return true;
        }

        session.QueueMessage("ERROR 포켓몬과 기술을 먼저 선택하세요.");
        return true;
    }
}
