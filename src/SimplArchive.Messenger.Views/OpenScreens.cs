using SimplArchive.Messenger.Client.Display;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// The screens on show that an arrival reloads (<see cref="MessengerScreens.Arrived"/>, #26): the list of conversations
/// while its page is in front, a thread while its page is in front or it is the top of the split's active pane. A screen
/// is in here only while it shows; one covered, closed or in a pane out of sight reads itself when it comes back. Each
/// reads through <see cref="ReloadOnArrival"/>, on its page's dispatcher, and waits while the app is not in front.
/// </summary>
internal sealed class OpenScreens
{
    private readonly Lock _gate = new();
    private readonly List<ReloadOnArrival> _showing = [];

    /// <summary>
    /// <paramref name="conversation"/>'s thread (null: the list) is on show on <paramref name="page"/>: arrivals for it
    /// run <paramref name="reload"/>, its pull-down's read, until the returned handle is disposed.
    /// </summary>
    public IDisposable Show(Page page, Guid? conversation, Func<Task> reload)
    {
        var screen = new ReloadOnArrival(conversation, () => page.Dispatcher.DispatchAsync(reload));
        var window = page.Window;
        void Paused(object? sender, EventArgs e) => screen.Paused();
        void Resumed(object? sender, EventArgs e) => screen.Resumed();
        if (window is not null)
        {
            window.Stopped += Paused;
            window.Resumed += Resumed;
        }

        lock (_gate)
        {
            _showing.Add(screen);
        }

        return new Shown(() =>
        {
            screen.Stop();
            if (window is not null)
            {
                window.Stopped -= Paused;
                window.Resumed -= Resumed;
            }

            lock (_gate)
            {
                _showing.Remove(screen);
            }
        });
    }

    /// <summary>Something new arrived, in <paramref name="conversation"/> where the host knows.</summary>
    public void Arrived(Guid? conversation)
    {
        ReloadOnArrival[] showing;
        lock (_gate)
        {
            showing = [.. _showing];
        }

        foreach (var screen in showing)
        {
            screen.Arrived(conversation);
        }
    }

    private sealed class Shown(Action hide) : IDisposable
    {
        private Action? _hide = hide;

        public void Dispose() => Interlocked.Exchange(ref _hide, null)?.Invoke();
    }
}
