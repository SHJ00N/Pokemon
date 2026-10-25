namespace Client;

abstract record ClientEvent;
sealed record UserInput(string? Text) : ClientEvent;
sealed record ServerMessage(string Text) : ClientEvent;
sealed record ConnectionClosed(string? Error) : ClientEvent;
