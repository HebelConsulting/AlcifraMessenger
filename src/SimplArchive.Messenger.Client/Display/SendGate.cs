namespace SimplArchive.Messenger.Client.Display;

/// <summary>
/// One send at a time, from the moment a message goes until its request answers (#26 follow-up: on a slow server every
/// further tap of Send queued another send, and one message arrived seven times). A send asked for while one is on its
/// way is not made — not queued, not repeated — and the screen shows Send disabled meanwhile (<see cref="Sending"/>).
/// </summary>
public sealed class SendGate
{
    private int _busy;

    /// <summary>Whether a send is on its way: Send (the button and the keyboard's key) is disabled while it is.</summary>
    public bool Sending => Volatile.Read(ref _busy) == 1;

    /// <summary><see cref="Sending"/> changed: the screen enables or disables Send.</summary>
    public event Action? Changed;

    /// <summary>
    /// Runs <paramref name="send"/> unless a send is on its way already; false when it was not run. Its failure is
    /// thrown to the caller, and Send is enabled again either way.
    /// </summary>
    public async Task<bool> SendAsync(Func<Task> send)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return false;
        }

        Changed?.Invoke();
        try
        {
            await send();
            return true;
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
            Changed?.Invoke();
        }
    }
}
