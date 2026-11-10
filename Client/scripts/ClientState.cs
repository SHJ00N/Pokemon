namespace Client;

abstract class ClientState
{
    public virtual string? BgmPath => null;
    protected abstract string Menu { get; }
    protected string? Notification { get; set; }

    public abstract Task<bool> HandleCommand(
        string input, StreamWriter writer, CancellationToken cancellationToken);

    // Runs on the client's single event loop so animated output never races with menus.
    public virtual void OnFrame() { }

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

    // 제한시간이 있는 화면은 남은 시간 표시만 고쳐 쓴다. (TimerTick 이벤트마다 호출)
    public virtual void UpdateTimer() { }

    public virtual void ShowMenu()
    {
        Console.Clear();
        Console.WriteLine(Menu);
        if (Notification != null)
            Console.WriteLine($"\n{Notification}");
    }

}
