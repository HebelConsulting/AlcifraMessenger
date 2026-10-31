using Alcifra.Messenger.Client.Display;
using Alcifra.Messenger.Client.Api;

namespace Alcifra.Messenger.Views;

/// <summary>
/// Where the Messages tile opens: the caller's conversations, and — where the host asked for the split and the window
/// is wide enough (<see cref="MessagesSplit"/>) — the open conversation beside them, as the desktop's Messages tab shows
/// it. Narrow, it is exactly the phone's list: a tapped conversation is a page of its own.
/// <para>
/// The page owns the selection: the list marks it, the pane renders it, and neither tells the other. A tapped row, a
/// new chat or group, a continued history: each becomes the selected conversation. New chat, New group and a group's
/// members take the pane in its place, the list staying. When the window narrows, what the pane shows becomes the pages
/// in front, as shown; when it widens while only pages of the Messenger's screens are in front of the list, they go back
/// into the pane.
/// </para>
/// </summary>
internal sealed class ConversationsPage : ContentPage
{
    private readonly MessengerScreens _screens;
    private readonly bool _split;
    private readonly ConversationsView _list;
    private readonly ScreenPane _pane;
    private readonly ColumnDefinition _listColumn = new(GridLength.Star);
    private readonly ColumnDefinition _dividerColumn = new(0);
    private readonly ColumnDefinition _paneColumn = new(0);
    private readonly BoxView _divider;
    private Guid? _selected;   // the conversation the pane shows; null while it shows none (or a new chat)
    private bool _splits;
    private bool _inFront;
    private bool _moving;
    private Guid? _visited;   // the conversation a page above showed last: marked for a moment when Back returns
    private int _flash;
    private IDisposable? _arrivals;   // while in front: an arrival reads the list again (#26)
    private readonly ToolbarItem _preferences;   // the caller's own settings, in the menu while the server offers them
    private ConversationList? _preferencesFrom;

    /// <param name="screens">The Messenger's screens.</param>
    /// <param name="split">Whether the host offers the split here; without it the page is the phone's list at any width.</param>
    /// <param name="selected">The conversation to show selected (a "new messages" push): beside the list, or pushed as
    /// its page once the window turns out narrow.</param>
    public ConversationsPage(MessengerScreens screens, bool split, Conversation? selected = null)
    {
        (_screens, _split) = (screens, split);
        screens.KeepClearOfKeyboard(this);   // the pane's compose field above the keyboard
        Title = ViewText.Get("Messages");
        _list = new ConversationsView(screens, this);
        foreach (var action in _list.Actions)
        {
            ToolbarItems.Add(new ToolbarItem { Text = action.Text, Command = new Command(async () => await action.Act()) });
        }

        _preferences = new ToolbarItem
        {
            Text = ViewText.Get("Preferences"),
            Order = ToolbarItemOrder.Secondary,
            Command = new Command(async () =>
            {
                if (_preferencesFrom is { } from)
                {
                    await Navigation.PushAsync(new PreferencesPage(_screens, from));
                }
            }),
        };

        _pane = new ScreenPane(screens, this) { IsVisible = false };
        _divider = new BoxView { Color = screens.Accent.WithAlpha(0.2f), IsVisible = false };
        var grid = new Grid { ColumnSpacing = 0, ColumnDefinitions = [_listColumn, _dividerColumn, _paneColumn] };
        grid.Add(_list, 0);
        grid.Add(_divider, 1);
        grid.Add(_pane, 2);
        Content = grid;
        if (split)
        {
            SizeChanged += (_, _) => Arrange();
            if (selected is not null)
            {
                Select(selected);
            }
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _inFront = true;
        _arrivals ??= _screens.Open.Show(this, conversation: null, () => _list.LoadAsync(quiet: true));
        Arrange();   // a narrowing while covered is done now
        await _list.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _inFront = false;
        _arrivals?.Dispose();
        _arrivals = null;
        _pane.Active = false;
        if (!Navigation.NavigationStack.Contains(this))
        {
            _pane.Clear();
        }
    }

    /// <summary>A row was tapped: selected beside the list in a wide window, its own page on a phone. A conversation
    /// shown already stays as it is; one with its members open over it goes back to its thread.</summary>
    public async Task OpenAsync(Conversation conversation)
    {
        if (!_splits)
        {
            await PushAsync([_screens.Thread(conversation)]);
            return;
        }

        if (_selected != conversation.Id || _pane.Root?.Id != conversation.Id || _pane.Depth > 1)
        {
            Select(conversation);
        }
    }

    /// <summary>New chat or New group: in the pane in place of the conversation, the list staying; a page on a phone.</summary>
    public async Task CreateAsync(PeopleMode mode, ConversationList list)
    {
        var people = _screens.People(mode, list);
        if (!_splits)
        {
            await PushAsync([people]);
            return;
        }

        _selected = null;
        _list.Mark(null);
        _pane.Show(people);
    }

    /// <summary>The list was read again: the pane keeps the same conversation, its fresh row marked, or empties when it
    /// left the list. A new chat being chosen in the pane stays as it is.</summary>
    public void Listed(IReadOnlyList<Conversation> rows)
    {
        if (_inFront && _visited is { } visited)
        {
            _visited = null;
            if (MessagesSplit.ReturnMark(visited, rows, _splits) is { } back)
            {
                Flash(back);
            }
        }

        if (_selected is not { } id)
        {
            return;
        }

        if (MessagesSplit.AfterReload(id, rows) is null)
        {
            Deselect();
        }
        else
        {
            _list.Mark(id);
        }
    }

    /// <summary>
    /// The list was read: <paramref name="list"/> offers the caller's own settings (#26), or null when it does not (a
    /// module that predates them) — then the menu has no entry for them.
    /// </summary>
    public void PreferencesOffered(ConversationList? list)
    {
        _preferencesFrom = list;
        var shown = ToolbarItems.Contains(_preferences);
        if (list is not null && !shown)
        {
            ToolbarItems.Add(_preferences);
        }
        else if (list is null && shown)
        {
            ToolbarItems.Remove(_preferences);
        }
    }

    /// <summary>A screen in the pane resulted in a conversation (a new chat or group, a continued history): it is the
    /// selected one now, and the list is read again to show it.</summary>
    public async Task ShowConversationAsync(Conversation conversation)
    {
        Select(conversation);
        await _list.LoadAsync();
    }

    /// <summary>The caller left the conversation in the pane: nothing is selected, and the list is read again.</summary>
    public async Task LeftAsync()
    {
        Deselect();
        await _list.LoadAsync();
    }

    /// <summary>A page above showed <paramref name="conversation"/>: on a phone the list marks it for a moment when Back
    /// returns, so the eye finds its place.</summary>
    public void Visited(Guid conversation) => _visited = conversation;

    /// <summary>Something the list shows changed (a thread in the pane was read): it is read again.</summary>
    public Task ReloadAsync() => _list.LoadAsync();

    // The mark comes and goes by itself; a selection or another return meanwhile owns the mark from then on.
    private async void Flash(Guid conversation)
    {
        var mine = ++_flash;
        _list.Mark(conversation);
        await Task.Delay(MessagesSplit.ReturnMarkFor);
        if (mine == _flash && !_splits)
        {
            _list.Mark(null);
        }
    }

    private void Select(Conversation conversation)
    {
        _flash++;
        _selected = conversation.Id;
        _list.Mark(conversation.Id);
        _pane.Show(_screens.Thread(conversation));
    }

    private void Deselect()
    {
        _selected = null;
        _list.Mark(null);
        _pane.Clear();
    }

    // The window's width decides, on every size change (rotation, a tablet's split screen); with the split not offered,
    // never. Narrowing with something in the pane moves it into pages — only while this page is in front; covered, it is
    // done when the page comes back.
    private void Arrange()
    {
        if (Width <= 0)
        {
            return;   // not laid out yet: neither layout is known
        }

        _splits = _split && MessagesSplit.Splits(Width);
        _listColumn.Width = _splits ? new GridLength(MessagesSplit.ListWidth(Width)) : GridLength.Star;
        _dividerColumn.Width = new GridLength(_splits ? 1 : 0);
        _paneColumn.Width = _splits ? GridLength.Star : new GridLength(0);
        _divider.IsVisible = _pane.IsVisible = _splits;
        _pane.Active = _splits && _inFront;
        if (_splits || _moving || !_pane.Shows || !_inFront || Navigation.NavigationStack.LastOrDefault() != this)
        {
            return;
        }

        var builds = _pane.Take();
        _selected = null;
        _list.Mark(null);
        _moving = true;
        Dispatcher.Dispatch(async () =>
        {
            try
            {
                await PushAsync(builds);
            }
            finally
            {
                _moving = false;
            }
        });
    }

    private async Task PushAsync(IReadOnlyList<Func<IScreenHost, ScreenView>> builds)
    {
        foreach (var build in builds)
        {
            await Navigation.PushAsync(new ScreenPage(_screens, build, this), animated: builds.Count == 1);
        }
    }

    // A page in front widened: when everything above this page is the Messenger's screens, they go back into the pane —
    // the bottom one selected where it is a thread — and the pages close. A photo or anything else in front stays.
    /// <summary>A page this one pushed changed its size (a rotation, a tablet's split screen).</summary>
    public async void Resized(ScreenPage page)
    {
        var stack = Navigation.NavigationStack;
        var at = stack.ToList().IndexOf(this);
        if (!_split || _moving || at < 0 || !MessagesSplit.Splits(page.Width) || stack[^1] != page)
        {
            return;
        }

        var above = stack.Skip(at + 1).ToList();
        if (above.Count == 0 || above.Any(p => p is not ScreenPage))
        {
            return;
        }

        var pages = above.Cast<ScreenPage>().ToList();
        foreach (var moved in pages)
        {
            moved.View.Close();   // built again in the pane: these views are let go of
        }

        _moving = true;
        try
        {
            _selected = pages[0].View.Subject?.Id;
            _list.Mark(_selected);
            _pane.Show([.. pages.Select(p => p.Build)]);
            foreach (var beneath in pages.Take(pages.Count - 1))
            {
                Navigation.RemovePage(beneath);
            }

            await Navigation.PopAsync(animated: false);
        }
        finally
        {
            _moving = false;
        }
    }
}
