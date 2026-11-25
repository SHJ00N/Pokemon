// 엔진(Battle)이 진행을 멈추고 다음에 기다리는 것
enum TurnProgress
{
    NextTurn,               // 새 턴의 행동 선택 (쓰러진 포켓몬 대신 내보낼 포켓몬 선택 포함)
    AwaitingMidTurnSwitch,  // 유턴·배턴터치 등으로 턴 도중에 교체할 포켓몬 선택
    Over                    // 전투 끝. 승패는 Battle.Outcome
}

enum BattleOutcome { HostWins, GuestWins, Draw }
