namespace Client;

abstract class ClientState
{
    protected abstract string Menu { get; }
    protected string? Notification { get; set; }

    public abstract Task<bool> HandleCommand(
        string input, StreamWriter writer, CancellationToken cancellationToken);

    public virtual ClientState HandleServerMessage(string message)
    {
        SetServerMessage(message.StartsWith("ERROR ") ? message[6..] : message);
        return this;
    }

    protected void SetServerMessage(string message)
    {
        Notification = $"[서버] {message}";
    }

    protected ClientState ChangeState(ClientState nextState)
    {
        nextState.Notification = Notification;
        return nextState;
    }

    public virtual void ShowMenu()
    {
        Console.Clear();
        Console.WriteLine(Menu);
        if (Notification != null)
            Console.WriteLine($"\n{Notification}");
    }

}
