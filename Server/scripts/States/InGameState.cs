using System.Text.Json;
using Pokemon.Protocol;

sealed class InGameState : ClientState
{
    public override bool HandleCommand(ClientSession session, RoomManager rooms, string command)
    {
        // 전투 중 기술 사용. 슬롯은 출전 중인 포켓몬의 기술 4개 중 몇 번째인지를 뜻한다.
        if (command.StartsWith("USE_MOVE "))
        {
            if (!int.TryParse(command[9..], out int slot) || slot is < 1 or > 4)
                session.QueueMessage("ERROR 기술 번호는 1~4입니다.");
            else
                rooms.UseMove(session, slot);
            return true;
        }

        // 전투 중 교체. 숫자는 팀 번호(1~3)다.
        if (command.StartsWith("SWITCH "))
        {
            if (!int.TryParse(command[7..], out int teamSlot) || teamSlot is < 1 or > 3)
                session.QueueMessage("ERROR 팀 번호는 1~3입니다.");
            else
                rooms.SwitchPokemon(session, teamSlot);
            return true;
        }

        if (command.StartsWith("SELECT_TEAM "))
        {
            try
            {
                // 포켓몬 3마리와 각각의 기술 4개 선택
                PokemonSelection[]? team = JsonSerializer.Deserialize<PokemonSelection[]>(command[12..]);
                rooms.SelectTeam(session, team);
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
