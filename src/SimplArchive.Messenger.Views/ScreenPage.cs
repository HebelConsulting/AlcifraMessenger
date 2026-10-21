using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// A screen as a page of its own, as on a phone: the same header the tablet's pane draws (the title, the screen's
/// actions as words, ⋮ for the rest) is drawn into the page's navigation bar, whose back arrow is the page's Back; the
/// bar's colours, the status bar and whatever the host adds to the toolbar (its way home) stay the host's. A screen it
/// opens is pushed over it. Built again from <see cref="Build"/> when a tablet's window widens and the page goes back
/// into the pane.
/// </summary>
internal sealed class ScreenPage : ContentPage, IScreenHost
{
    private readonly MessengerScreens _screens;
    private readonly ConversationsPage? _list;
    private ScreenHeader _header;

    /// <param name="screens">The Messenger's screens.</param>
    /// <param name="build">Builds the screen with this page as its host.</param>
    /// <param name="list">The conversations page beneath, told what happened here (a conversation shown, created or
    /// left, a size change); passed on to every page this one opens. Null: none is.</param>
    public ScreenPage(MessengerScreens screens, Func<IScreenHost, ScreenView> build, ConversationsPage? list)
    {
        (_screens, Build, _list) = (screens, build, list);
        _header = Header(Colors.White);
        View = build(this);
        View.Changed += Draw;
        Draw();
        Content = View;
        if (list is not null)
        {
            SizeChanged += (_, _) => list.Resized(this);
        }
    }

    /// <summary>How the screen is built: the same again, in a page or in the pane.</summary>
    public Func<IScreenHost, ScreenView> Build { get; }

    /// <summary>The screen this page shows.</summary>
    public ScreenView View { get; }

    Page IScreenHost.Page => this;

    // In the bar's own text colour where the host set one.
    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (Parent is NavigationPage { BarTextColor: { } ink })
        {
            _header = Header(ink);
            Draw();
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (View.Subject is { } conversation)
        {
            _list?.Visited(conversation.Id);   // what the list marks for a moment when Back returns to it
        }

        await View.AppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!Navigation.NavigationStack.Contains(this))
        {
            View.Close();
        }
    }

    Task IScreenHost.OpenAsync(Func<IScreenHost, ScreenView> screen) => Navigation.PushAsync(new ScreenPage(_screens, screen, _list));

    Task IScreenHost.BackAsync() => Navigation.PopAsync();

    // The conversation takes this page's place; the list beneath is read at once, so Back finds it there already.
    async Task IScreenHost.ShowConversationAsync(Conversation conversation)
    {
        Navigation.InsertPageBefore(new ScreenPage(_screens, _screens.Thread(conversation), _list), this);
        await Navigation.PopAsync();
        if (_list is not null)
        {
            await _list.ReloadAsync();
        }
    }

    // Out of the conversation: back past its screens (its thread, its members), which can no longer be opened, to the list.
    async Task IScreenHost.LeftAsync()
    {
        var stack = Navigation.NavigationStack;
        var at = stack.Count - 2;
        while (at >= 0 && stack[at] is ScreenPage beneath)
        {
            Navigation.RemovePage(beneath);
            at--;
        }

        await Navigation.PopAsync();
    }

    Task IScreenHost.ConversationsChangedAsync() => Task.CompletedTask;   // the list beneath reads itself when it is back

    private ScreenHeader Header(Color ink)
    {
        var header = new ScreenHeader(ink, this, inBar: true) { HorizontalOptions = LayoutOptions.Fill };
        NavigationPage.SetTitleView(this, header);
        return header;
    }

    private void Draw()
    {
        Title = View.Title;
        _header.Show(View.Title, View.Actions, back: null);
    }
}
