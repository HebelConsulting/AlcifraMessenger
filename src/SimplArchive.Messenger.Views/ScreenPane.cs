using SimplArchive.Messenger.Client.Display;
using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// The right pane of the split, on a tablet: the screen the shell puts into it — a conversation's thread, or New chat,
/// New group — under a compact header (Back where a screen was opened over another, the title, the screen's actions, ⋮
/// for the secondary ones). A screen opened from it (a group's members, adding someone) takes the pane in turn, and Back
/// returns. It shows what the shell tells it and keeps no selection of its own: each screen is a new view, the previous
/// one let go of and taken out first, so nothing of it shows for the next and no view is laid out twice.
/// </summary>
internal sealed class ScreenPane : ContentView
{
    private readonly MessengerScreens _screens;
    private readonly ConversationsPage _shell;
    private readonly List<Entry> _stack = [];
    private readonly ContentView _body = new();
    private readonly ScreenHeader _header;
    private readonly View _placeholder;
    private bool _active;

    public ScreenPane(MessengerScreens screens, ConversationsPage shell)
    {
        (_screens, _shell) = (screens, shell);
        _header = new ScreenHeader(screens.Accent, shell, inBar: false);
        _placeholder = new Label
        {
            Text = ViewText.Get("SelectConversation"),
            Opacity = 0.6,
            FontSize = 16,
            HorizontalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Margin = 32,
        };
        var grid = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)] };
        grid.Add(_header, 0, 0);
        grid.Add(_body, 0, 1);
        Content = grid;
        _body.Content = _placeholder;
    }

    /// <summary>Whether anything is shown: a screen to move into a page when the window narrows.</summary>
    public bool Shows => _stack.Count > 0;

    /// <summary>How many screens are shown: more than one where a screen was opened over the first.</summary>
    public int Depth => _stack.Count;

    /// <summary>The conversation whose thread the pane shows at its bottom, if it shows one.</summary>
    public Conversation? Root => _stack.Count > 0 ? _stack[0].View.Subject : null;

    /// <summary>
    /// Whether the pane is on screen and its page in front: a screen shown while it is reads what it shows at once,
    /// and becoming so reads the top screen again (the page came back, the window widened).
    /// </summary>
    public bool Active
    {
        get => _active;
        set
        {
            var was = _active;
            _active = value;
            if (_stack.Count > 0)
            {
                _stack[^1].View.SetShowing(value);
            }

            if (value && !was && _stack.Count > 0)
            {
                _ = _stack[^1].View.AppearingAsync();
            }
        }
    }

    /// <summary>Shows <paramref name="screen"/> in place of whatever was shown, every screen of before let go of.</summary>
    public void Show(Func<IScreenHost, ScreenView> screen) => Show([screen]);

    /// <summary>Shows <paramref name="screens"/>, the last on top (pages moved back into the pane as the window widened).</summary>
    public void Show(IReadOnlyList<Func<IScreenHost, ScreenView>> screens)
    {
        Clear();
        foreach (var screen in screens)
        {
            _stack.Add(new Entry(this, screen));
        }

        Draw();
    }

    /// <summary>Shows nothing: the placeholder.</summary>
    public void Clear()
    {
        foreach (var entry in _stack)
        {
            entry.View.Close();
            entry.View.Changed -= DrawHeader;
        }

        _stack.Clear();
        Draw();
    }

    /// <summary>Takes out every screen shown, bottom first, as they would be built again in pages of their own.</summary>
    public IReadOnlyList<Func<IScreenHost, ScreenView>> Take()
    {
        var builds = _stack.Select(entry => entry.Build).ToList();
        Clear();
        return builds;
    }

    private void Push(Func<IScreenHost, ScreenView> screen)
    {
        _stack.Add(new Entry(this, screen));
        Draw();
    }

    private void Pop(Entry entry)
    {
        if (_stack.Count < 2 || _stack[^1] != entry)
        {
            return;   // not the top, or the screen the shell put in: Back has nowhere to go
        }

        entry.View.Close();
        entry.View.Changed -= DrawHeader;
        _stack.RemoveAt(_stack.Count - 1);
        Draw();
    }

    // The top screen goes in, the one before it out (a MAUI view laid out twice crashes); it reads what it shows when the
    // pane is active, and otherwise once it becomes so.
    private void Draw()
    {
        foreach (var entry in _stack)
        {
            entry.View.Changed -= DrawHeader;
            entry.View.SetShowing(false);   // only the top is on show, and only while the pane is active
        }

        _body.Content = null;
        if (_stack.Count == 0)
        {
            _header.Hide();
            _body.Content = _placeholder;
            return;
        }

        var top = _stack[^1].View;
        top.Changed += DrawHeader;
        DrawHeader();
        _body.Content = top;
        if (_active)
        {
            top.SetShowing(true);
            _ = top.AppearingAsync();
        }
    }

    private void DrawHeader()
    {
        if (_stack.Count == 0)
        {
            _header.Hide();
            return;
        }

        var top = _stack[^1];
        _header.Show(top.View.Title, top.View.Actions, _stack.Count > 1 ? top.BackAsync : null);
    }

    // One screen of the pane, and its host: what the screen says happened, the pane and the shell carry out — unless the
    // screen was let go of meanwhile, when it is too late to mean anything.
    private sealed class Entry : IScreenHost
    {
        private readonly ScreenPane _pane;

        public Entry(ScreenPane pane, Func<IScreenHost, ScreenView> build)
        {
            (_pane, Build) = (pane, build);
            View = build(this);
        }

        public Func<IScreenHost, ScreenView> Build { get; }

        public ScreenView View { get; }

        public Page Page => _pane._shell;

        public Task OpenAsync(Func<IScreenHost, ScreenView> screen)
        {
            if (!View.IsClosed)
            {
                _pane.Push(screen);
            }

            return Task.CompletedTask;
        }

        public Task BackAsync()
        {
            _pane.Pop(this);
            return Task.CompletedTask;
        }

        public Task ShowConversationAsync(Conversation conversation) =>
            View.IsClosed ? Task.CompletedTask : _pane._shell.ShowConversationAsync(conversation);

        public Task LeftAsync() => View.IsClosed ? Task.CompletedTask : _pane._shell.LeftAsync();

        public Task ConversationsChangedAsync() => View.IsClosed ? Task.CompletedTask : _pane._shell.ReloadAsync();
    }
}
