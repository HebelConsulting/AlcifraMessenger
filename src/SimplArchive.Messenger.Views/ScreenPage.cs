using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// A screen as a page of its own, as on a phone: its title and actions in the toolbar, a screen it opens pushed over
/// it. Built again from <see cref="Build"/> when a tablet's window widens and the page goes back into the pane.
/// </summary>
internal sealed class ScreenPage : ContentPage, IScreenHost
{
    private readonly MessengerScreens _screens;
    private readonly Action<ScreenPage>? _resized;

    /// <param name="screens">The Messenger's screens.</param>
    /// <param name="build">Builds the screen with this page as its host.</param>
    /// <param name="resized">Told when the page's size changed, by a split that pushed it (null: none did); passed on to
    /// every page this one opens.</param>
    public ScreenPage(MessengerScreens screens, Func<IScreenHost, ScreenView> build, Action<ScreenPage>? resized)
    {
        (_screens, Build, _resized) = (screens, build, resized);
        View = build(this);
        View.Changed += Draw;
        Draw();
        Content = View;
        if (resized is not null)
        {
            SizeChanged += (_, _) => resized(this);
        }
    }

    /// <summary>How the screen is built: the same again, in a page or in the pane.</summary>
    public Func<IScreenHost, ScreenView> Build { get; }

    /// <summary>The screen this page shows.</summary>
    public ScreenView View { get; }

    Page IScreenHost.Page => this;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
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

    Task IScreenHost.OpenAsync(Func<IScreenHost, ScreenView> screen) => Navigation.PushAsync(new ScreenPage(_screens, screen, _resized));

    Task IScreenHost.BackAsync() => Navigation.PopAsync();

    async Task IScreenHost.ShowConversationAsync(Conversation conversation)
    {
        Navigation.InsertPageBefore(new ScreenPage(_screens, _screens.Thread(conversation), _resized), this);
        await Navigation.PopAsync();
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

    private void Draw()
    {
        Title = View.Title;
        ToolbarItems.Clear();
        foreach (var action in View.Actions)
        {
            ToolbarItems.Add(new ToolbarItem
            {
                Text = action.Text,
                Order = action.Secondary ? ToolbarItemOrder.Secondary : ToolbarItemOrder.Default,
                Command = new Command(async () => await action.Act()),
            });
        }
    }
}
