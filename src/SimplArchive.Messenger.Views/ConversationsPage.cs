using SimplArchive.Messenger.Client.Display;
using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Views;

/// <summary>The caller's conversations, with unread counts; new chats and groups from the toolbar.</summary>
internal sealed class ConversationsPage : ContentPage
{
    private readonly MessengerScreens _screens;
    private readonly CollectionView _list;
    private readonly Label _empty;
    private ConversationList? _conversations;

    public ConversationsPage(MessengerScreens screens)
    {
        _screens = screens;
        Title = ViewText.Get("Messages");
        ToolbarItems.Add(new ToolbarItem { Text = ViewText.Get("NewChat"), Command = new Command(async () => await OpenAsync(screens.NewChat)) });
        ToolbarItems.Add(new ToolbarItem { Text = ViewText.Get("NewGroup"), Command = new Command(async () => await OpenAsync(screens.NewGroup)) });

        _empty = new Label { Text = ViewText.Get("NoConversations"), IsVisible = false, Margin = 24, HorizontalTextAlignment = TextAlignment.Center };
        _list = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(() => Row(screens.Accent)),
        };
        _list.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is Conversation chosen)
            {
                _list.SelectedItem = null;
                await Navigation.PushAsync(screens.Thread(chosen));
            }
        };
        var refresh = new RefreshView { Content = _list };
        refresh.Command = new Command(async () =>
        {
            await LoadAsync();
            refresh.IsRefreshing = false;
        });
        Content = new Grid { Children = { refresh, _empty } };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _conversations = await _screens.Session.Api.ConversationsAsync();
            _list.ItemsSource = _conversations.Items;
            _empty.IsVisible = _conversations.Items.Count == 0;
        }
        catch (Exception exception)
        {
            await MessengerScreens.ShowAsync(this, exception);
        }
    }

    private async Task OpenAsync(Func<ConversationList, Page> page)
    {
        if (_conversations is { } list)
        {
            await Navigation.PushAsync(page(list));
        }
    }

    // A conversation's row: its title, what kind it is, and an unread badge in the accent. Filled from its binding context
    // rather than bound by path, so nothing depends on reflection.
    private static View Row(Color accent)
    {
        var title = new Label { FontSize = 17, LineBreakMode = LineBreakMode.TailTruncation };
        var kind = new Label { FontSize = 13, TextColor = Colors.Gray };
        var count = new Label { TextColor = Colors.White, FontSize = 13, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        var badge = new Border
        {
            BackgroundColor = accent,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 11 },
            MinimumWidthRequest = 22,
            HeightRequest = 22,
            Padding = new Thickness(6, 0),
            VerticalOptions = LayoutOptions.Center,
            Content = count,
        };

        var grid = new Grid { Padding = new Thickness(16, 10), ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        grid.Add(new VerticalStackLayout { Children = { title, kind } }, 0);
        grid.Add(badge, 1);
        grid.BindingContextChanged += (_, _) =>
        {
            if (grid.BindingContext is Conversation c)
            {
                title.Text = ThreadRows.Title(c);
                kind.Text = c.IsDirect ? string.Empty : ViewText.Get("GroupMembers", c.Members.Count);
                kind.IsVisible = !c.IsDirect;
                count.Text = c.Unread.ToString(System.Globalization.CultureInfo.CurrentCulture);
                badge.IsVisible = c.Unread > 0;
            }
        };
        return grid;
    }
}
