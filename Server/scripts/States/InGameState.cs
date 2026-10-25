using System.Text.Json;
using Pokemon.Protocol;

sealed class InGameState : ClientState
{
    public override bool HandleCommand(ClientSession session, RoomManager rooms, string command)
    {
        /*
        서버의 인게임 커맨드 처리 
        전투 중 기술 선택
        if (command.StartsWith("USE_MOVE "))
        {
            if (!int.TryParse(command[9..], out int slot)
                || slot is < 1 or > 4)
            {
                session.QueueMessage("ERROR 기술 번호는 1~4입니다.");
                return true;
            }

            // 선택을 저장하고, 양쪽 선택이 모이면 전투를 계산한다.
            rooms.SelectMove(session, slot); // RoomManager 클래스에 SelectMove 정의해야 함.
            return true;
        }
        */

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
