using System.Text.Json;
using Pokemon.Protocol;

sealed class InGameState : ClientState
{
    public override bool HandleCommand(ClientSession session, RoomManager rooms, string command)
    {
        // USE_MOVE <기술 슬롯 1~4> [MEGA [폼 ID]]. 턴 도중 교체는 SWITCH로 고른다.
        if (command.StartsWith("USE_MOVE "))
        {
            string[] parts = command.Split(' ', 4);
            if (!int.TryParse(parts[1], out int slot) || slot is < 1 or > PokemonSelection.MaxMoves)
                session.QueueMessage("ERROR 기술 번호는 1~4입니다.");
            else if (parts.Length == 2)
                rooms.UseMove(session, slot);
            else
            {
                int formId = 0;
                if (parts[2] != "MEGA" || (parts.Length == 4 && (!int.TryParse(parts[3], out formId) || formId <= 0)))
                    session.QueueMessage("ERROR 메가진화 요청 형식이 올바르지 않습니다.");
                else
                    rooms.UseMove(session, slot, megaEvolve: true, megaFormId: parts.Length == 4 ? formId : null);
            }
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
                // 포켓몬 3마리와 각각의 기술 최대 4개 선택
                PokemonSelection[]? team = JsonSerializer.Deserialize<PokemonSelection[]>(command[12..]);
                rooms.SelectTeam(session, team);
            }
            catch (JsonException)
            {
                session.QueueMessage("ERROR 포켓몬 선택 요청 형식이 올바르지 않습니다.");
            }

            return true;
        }

        switch (command)
        {
            case "SURRENDER":   // 전투 중 항복. 상대가 이기고 양쪽 모두 결과 화면으로 간다.
                rooms.Surrender(session);
                return true;
            case "REMATCH":     // 결과 화면에서 재도전 신청
                rooms.RequestRematch(session);
                return true;
            case "LEAVE":       // 방에서 나가기. 전투 중이면 항복으로 처리한다.
                rooms.LeaveRoom(session, notify: true);
                return true;
        }

        session.QueueMessage("ERROR 포켓몬과 기술을 먼저 선택하세요.");
        return true;
    }
}
