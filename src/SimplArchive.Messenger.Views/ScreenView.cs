using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// An action of a screen: in a page's toolbar, or in the pane's header beside the screen's title. A secondary one goes
/// into the page's overflow menu, or behind the pane's ⋮.
/// </summary>
internal sealed record ScreenAction(string Text, Func<Task> Act, bool Secondary = false);

/// <summary>
/// What hosts a screen — a page of its own (<see cref="ScreenPage"/>) on a phone, or the right pane of the split
/// (<see cref="ScreenPane"/>) on a tablet. A screen says what happened; its host decides what that means where it is.
/// It is a constructor argument of every screen, so a screen cannot be built without one.
/// </summary>
internal interface IScreenHost
{
    /// <summary>The page the screen is on: its dialogs go over it, and a photo opens over it.</summary>
    Page Page { get; }

    /// <summary>Opens a screen over this one (a group's members, adding someone): a page pushed, or the pane's next.</summary>
    Task OpenAsync(Func<IScreenHost, ScreenView> screen);

    /// <summary>This screen is done (a member was added): back to the one beneath it.</summary>
    Task BackAsync();

    /// <summary>This screen's outcome is a conversation (a new chat or group, a continued history): it takes this
    /// screen's place, and on a tablet becomes the selected conversation.</summary>
    Task ShowConversationAsync(Conversation conversation);

    /// <summary>The caller left the conversation this screen is of: back to the list, its screens closed.</summary>
    Task LeftAsync();

    /// <summary>Something the list shows may have changed (a thread was read, a message sent): a list beside the screen
    /// is read again, one beneath it is when it comes back.</summary>
    Task ConversationsChangedAsync();
}

/// <summary>
/// One of the Messenger's screens as a view, so it lives in a page of its own or in the split's right pane: its title
/// and actions are drawn by its host, and its host is told what happened.
/// </summary>
internal abstract class ScreenView : ContentView
{
    private string _title = string.Empty;
    private IReadOnlyList<ScreenAction> _actions = [];
    private IDisposable? _arrivals;

    protected ScreenView(MessengerScreens screens, IScreenHost host) => (Screens, Host) = (screens, host);

    /// <summary>Its title or its actions changed: the host draws them again.</summary>
    public event Action? Changed;

    /// <summary>The screen's title.</summary>
    public string Title
    {
        get => _title;
        protected set
        {
            _title = value;
            Changed?.Invoke();
        }
    }

    /// <summary>The screen's actions, in order.</summary>
    public IReadOnlyList<ScreenAction> Actions
    {
        get => _actions;
        protected set
        {
            _actions = value;
            Changed?.Invoke();
        }
    }

    /// <summary>The conversation the screen is of, where it is one's thread: what the list marks when it is in the pane.</summary>
    public virtual Conversation? Subject => null;

    /// <summary>Whether the host let go of the screen: a read that finishes late shows nothing and says nothing.</summary>
    public bool IsClosed { get; private set; }

    protected MessengerScreens Screens { get; }

    protected IScreenHost Host { get; }

    /// <summary>The screen came to the front (again): it reads what it shows.</summary>
    public virtual Task AppearingAsync() => Task.CompletedTask;

    /// <summary>The host let go of the screen, for good.</summary>
    public void Close()
    {
        IsClosed = true;
        SetShowing(false);
    }

    /// <summary>
    /// Whether the screen is on show — its page in front, or the top of the split's active pane — as its host knows: while
    /// it is, an arrival the screen is concerned by reads it again (<see cref="MessengerScreens.Arrived"/>, #26).
    /// </summary>
    public void SetShowing(bool showing)
    {
        if (!showing || IsClosed)
        {
            _arrivals?.Dispose();
            _arrivals = null;
        }
        else if (_arrivals is null && ReloadsOnArrival is { } reloads)
        {
            _arrivals = Screens.Open.Show(Host.Page, reloads.Conversation, reloads.Read);
        }

        Showing(showing && !IsClosed);
    }

    /// <summary>The screen came on show or left it (also for good): a thread listens for "is typing" only meanwhile.</summary>
    protected virtual void Showing(bool showing)
    {
    }

    /// <summary>What an arrival reads again while the screen is on show: a thread's conversation and its pull-down's read.
    /// Null for a screen no arrival changes (a group's members, the people picker).</summary>
    protected virtual (Guid Conversation, Func<Task> Read)? ReloadsOnArrival => null;

    /// <summary>Shows a refusal or a failure over the screen's page — not once the screen was let go of.</summary>
    protected Task ShowAsync(Exception exception) => IsClosed ? Task.CompletedTask : MessengerScreens.ShowAsync(Host.Page, exception);
}
