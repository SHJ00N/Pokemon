// 전투에 참여한 플레이어 한 명(또는 AI). 한 편에 여러 명이 있을 수 있다(2:2).
sealed class BattlePlayer
{
    public ClientSession? Session { get; }            // AI면 null
    public IReadOnlyList<int> TeamIndices { get; }    // 편의 Team에서 이 플레이어가 맡은 번호
    public bool HasMegaEvolved { get; set; }          // 이 전투에서 메가진화를 썼는지 (플레이어마다 한 번)

    public BattlePlayer(ClientSession? session, int[] teamIndices)
    {
        Session = session;
        TeamIndices = teamIndices;
    }
}
