using SimplArchive.Messenger.Client.Display;
using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// The caller's conversations, with unread counts; new chats and groups among its actions. What a tap means is the
/// shell's (<see cref="ConversationsPage"/>): the thread pushed on a phone, the conversation selected beside the list on
/// a tablet. On a tablet it marks the row the pane shows, as told (<see cref="Mark"/>), and keeps no selection of its own.
/// </summary>
internal sealed class ConversationsView : ContentView
{
    private readonly MessengerScreens _screens;
    private readonly ConversationsPage _shell;
    private readonly CollectionView _list;
    private readonly Label _empty;
    private readonly List<Grid> _rows = [];   // the rows laid out so far: the list recycles them, so they are few
    private readonly Color _marked;
    private ConversationList? _conversations;
    private Guid? _markedId;

    public ConversationsView(MessengerScreens screens, ConversationsPage shell)
    {
        (_screens, _shell) = (screens, shell);
        _marked = screens.Accent.WithAlpha(0.2f);
        Actions =
        [
            new(ViewText.Get("NewChat"), () => CreateAsync(PeopleMode.Chat)),
            new(ViewText.Get("NewGroup"), () => CreateAsync(PeopleMode.Group)),
        ];

        _empty = new Label { Text = ViewText.Get("NoConversations"), IsVisible = false, Margin = 24, HorizontalTextAlignment = TextAlignment.Center };
        _list = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(Row),
        };
        _list.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is Conversation chosen)
            {
                _list.SelectedItem = null;   // a tap, not a state: the mark is the shell's
                await _shell.OpenAsync(chosen);
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

    /// <summary>New chat and New group: in the toolbar of the page the list is on.</summary>
    public IReadOnlyList<ScreenAction> Actions { get; }

    /// <summary>Reads the conversations again; the shell is told what is listed now.</summary>
    public async Task LoadAsync()
    {
        try
        {
            _conversations = await _screens.Session.Api.ConversationsAsync();
            _list.ItemsSource = _conversations.Items;
            _empty.IsVisible = _conversations.Items.Count == 0;
            _shell.Listed(_conversations.Items);
        }
        catch (Exception exception)
        {
            await MessengerScreens.ShowAsync(_shell, exception);
        }
    }

    /// <summary>Marks the conversation with <paramref name="id"/>, and no other (null: none).</summary>
    public void Mark(Guid? id)
    {
        _markedId = id;
        foreach (var row in _rows)
        {
            Paint(row);
        }
    }

    private async Task CreateAsync(PeopleMode mode)
    {
        if (_conversations is { } list)
        {
            await _shell.CreateAsync(mode, list);
        }
    }

    private void Paint(Grid row) =>
        row.BackgroundColor = _markedId is { } id && row.BindingContext is Conversation c && c.Id == id ? _marked : Colors.Transparent;

    // A conversation's row: its title, what kind it is, and an unread badge in the accent. Filled from its binding context
    // rather than bound by path, so nothing depends on reflection.
    private View Row()
    {
        var title = new Label { FontSize = 17, LineBreakMode = LineBreakMode.TailTruncation };
        var kind = new Label { FontSize = 13, TextColor = Colors.Gray };
        var count = new Label { TextColor = Colors.White, FontSize = 13, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        var badge = new Border
        {
            BackgroundColor = _screens.Accent,
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
        _rows.Add(grid);
        grid.BindingContextChanged += (_, _) =>
        {
            if (grid.BindingContext is Conversation c)
            {
                title.Text = ThreadRows.Title(c);
                // History says so in the list: one the caller left, or one restored from a backup.
                kind.Text = c.Left ? ViewText.Get("LeftMark") : c.Restored ? ViewText.Get("RestoredMark")
                    : c.IsDirect ? string.Empty : ViewText.Get("GroupMembers", c.Members.Count);
                kind.IsVisible = c.Left || c.Restored || !c.IsDirect;
                count.Text = c.Unread.ToString(System.Globalization.CultureInfo.CurrentCulture);
                badge.IsVisible = c.Unread > 0;
            }

            Paint(grid);
        };
        return grid;
    }
}
