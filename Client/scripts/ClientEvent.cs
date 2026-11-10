namespace Client;

abstract record ClientEvent;
sealed record UserInput(string? Text) : ClientEvent;
sealed record ServerMessage(string Text) : ClientEvent;
sealed record ConnectionClosed(string? Error) : ClientEvent;
sealed record AudioUpdate : ClientEvent;
sealed record TimerTick : ClientEvent;     // 제한시간 표시를 고쳐 쓸 때
