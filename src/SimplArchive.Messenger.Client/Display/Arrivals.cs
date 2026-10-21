namespace SimplArchive.Messenger.Client.Display;

/// <summary>
/// Which open screen a "something new arrived" from the host reloads (a push, SimplArchiveMessenger #26): the list of
/// conversations always, a thread only for its own conversation — or for any, when the host cannot say which. Free of
/// any user interface, so the rule is tested where the screens cannot be.
/// </summary>
public static class Arrivals
{
    /// <summary>How long a screen waits after the first arrival before it reads again, so a burst is read once.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Whether an arrival in <paramref name="arrivedIn"/> concerns a screen of <paramref name="screen"/>: null for the
    /// list, which every arrival may change, else the thread's conversation. An arrival the host cannot place (null)
    /// concerns every screen.
    /// </summary>
    public static bool Concerns(Guid? screen, Guid? arrivedIn) =>
        screen is not { } conversation || arrivedIn is not { } arrived || conversation == arrived;
}

/// <summary>
/// One open screen's reload on arrival: arrivals close together are read once, an arrival while a read runs is read
/// once more after it, and an arrival while the app is not in front waits until it is (<see cref="Resumed"/>) — never a
/// read, and never a hardware-key prompt, behind the user's back. Once <see cref="Stop"/>ped (the screen left the front)
/// it reads nothing more, a pending read included; the screen reads itself when it comes back.
/// </summary>
/// <param name="conversation">The thread's conversation; null for the list.</param>
/// <param name="reload">The screen's own read, the one its pull-down runs. Its failures are its own: a thrown one is
/// dropped here, since nobody asked for this read.</param>
/// <param name="delay">Waits out <see cref="Arrivals.Window"/>; tests pass their own clock.</param>
public sealed class ReloadOnArrival(Guid? conversation, Func<Task> reload, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private bool _scheduled;   // a read is waiting out the window: further arrivals are part of it
    private bool _running;
    private bool _again;       // an arrival during the read: one more read after it
    private bool _missed;      // an arrival while not in front: read when back
    private bool _inFront = true;

    /// <summary>The thread's conversation; null for the list.</summary>
    public Guid? Conversation => conversation;

    /// <summary>Something new arrived, in <paramref name="arrivedIn"/> where the host knows: read again if it concerns
    /// this screen.</summary>
    public void Arrived(Guid? arrivedIn)
    {
        if (!Arrivals.Concerns(conversation, arrivedIn))
        {
            return;
        }

        lock (_gate)
        {
            if (_stop.IsCancellationRequested)
            {
                return;
            }

            if (!_inFront)
            {
                _missed = true;
                return;
            }
        }

        Request();
    }

    /// <summary>The app left the front (the screen locked, another app): arrivals wait.</summary>
    public void Paused()
    {
        lock (_gate)
        {
            _inFront = false;
        }
    }

    /// <summary>The app is in front again: what arrived meanwhile is read now.</summary>
    public void Resumed()
    {
        bool missed;
        lock (_gate)
        {
            _inFront = true;
            missed = _missed;
            _missed = false;
        }

        if (missed)
        {
            Request();
        }
    }

    /// <summary>The screen left the front for good or for now: nothing more is read, a pending read included.</summary>
    public void Stop() => _stop.Cancel();

    private void Request()
    {
        lock (_gate)
        {
            if (_stop.IsCancellationRequested || _scheduled)
            {
                return;
            }

            if (_running)
            {
                _again = true;
                return;
            }

            _scheduled = true;
        }

        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        while (true)
        {
            try
            {
                await _delay(Arrivals.Window, _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (_gate)
            {
                _scheduled = false;
                if (_stop.IsCancellationRequested)
                {
                    return;
                }

                _running = true;
            }

            try
            {
                await reload();
            }
            catch (Exception)
            {
                // Nobody asked for this read: a failure is not put in front of anyone. The next arrival or pull tries again.
            }

            lock (_gate)
            {
                _running = false;
                var again = _again && !_stop.IsCancellationRequested;
                _again = false;
                if (again && !_inFront)
                {
                    _missed = true;   // arrived during the read, and the app left the front since
                    return;
                }

                if (!again)
                {
                    return;
                }

                _scheduled = true;
            }
        }
    }
}
