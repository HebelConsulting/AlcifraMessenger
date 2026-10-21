using SimplArchive.Messenger.Client.Display;
using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Views;

/// <summary>What the people picker is for.</summary>
internal enum PeopleMode
{
    Chat,
    Group,
    AddMember,
}

/// <summary>
/// Choosing who to talk to: one person for a chat, or a name and several people for a group. Only people with an enrolled
/// key are offered, and the screen says why. A page of its own on a phone; on a tablet it takes the right pane, the
/// conversations staying beside it.
/// </summary>
internal sealed class PeopleView : ScreenView
{
    private readonly ConversationList? _list;
    private readonly PeopleMode _mode;
    private readonly Conversation? _target;
    private readonly CollectionView _people;
    private readonly Entry? _name;
    private readonly HashSet<Guid> _chosen = [];
    private IReadOnlyList<Person> _found = [];

    public PeopleView(MessengerScreens screens, IScreenHost host, PeopleMode mode, ConversationList? list = null, Conversation? group = null)
        : base(screens, host)
    {
        (_mode, _list, _target) = (mode, list, group);
        Title = ViewText.Get(mode switch { PeopleMode.Group => "NewGroup", PeopleMode.AddMember => "AddMember", _ => "NewChat" });
        if (mode == PeopleMode.Group)
        {
            Actions = [new(ViewText.Get("Create"), CreateGroupAsync)];
            _name = new Entry { Placeholder = ViewText.Get("GroupName") };
        }

        var search = new SearchBar { Placeholder = ViewText.Get("Search") };
        search.SearchButtonPressed += async (_, _) => await SearchAsync(search.Text ?? string.Empty);
        search.TextChanged += async (_, e) =>
        {
            if ((e.NewTextValue?.Length ?? 0) is 0 or >= 2)
            {
                await SearchAsync(e.NewTextValue ?? string.Empty);
            }
        };

        _people = new CollectionView
        {
            SelectionMode = mode == PeopleMode.Group ? SelectionMode.Multiple : SelectionMode.Single,
            ItemTemplate = new DataTemplate(Row),
        };
        _people.SelectionChanged += async (_, e) =>
        {
            if (_mode == PeopleMode.Group)
            {
                _chosen.Clear();
                _chosen.UnionWith(e.CurrentSelection.OfType<Person>().Select(p => p.Id));
            }
            else if (e.CurrentSelection.FirstOrDefault() is Person person)
            {
                _people.SelectedItem = null;
                await (_mode == PeopleMode.AddMember ? AddMemberAsync(person) : StartChatAsync(person));
            }
        };

        var hint = new Label { Text = ViewText.Get("PeopleHint"), FontSize = 13, TextColor = Colors.Gray, Margin = new Thickness(16, 4) };
        var layout = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)] };
        if (_name is not null)
        {
            layout.Add(new ContentView { Padding = new Thickness(12, 8, 12, 0), Content = _name }, 0, 0);
        }

        layout.Add(search, 0, 1);
        layout.Add(hint, 0, 2);
        layout.Add(_people, 0, 3);
        Content = layout;
    }

    public override Task AppearingAsync() => SearchAsync(string.Empty);

    private async Task SearchAsync(string term)
    {
        try
        {
            // Neither the caller nor, when adding, anyone already in the group.
            var already = _target?.Members.Select(m => m.UserId).ToHashSet() ?? [];
            var found = (await Screens.Session.Api.PeopleAsync(term)).Items;
            if (IsClosed)
            {
                return;
            }

            _found = [.. found.Where(p => p.Id != Screens.Session.Me && !already.Contains(p.Id))];
            _people.ItemsSource = _found;
        }
        catch (Exception exception)
        {
            await ShowAsync(exception);
        }
    }

    private async Task AddMemberAsync(Person person)
    {
        if (!await Host.Page.DisplayAlertAsync(ViewText.Get("AddMember"), ViewText.Get("AddConfirm", person.DisplayName), ViewText.Get("Yes"), ViewText.Get("No")))
        {
            return;
        }

        try
        {
            // A new key for everyone, the newcomer included: they read from now on, nothing from before.
            await Screens.Session.AddAsync(_target!, person);
            await Host.BackAsync();
        }
        catch (Exception exception)
        {
            await ShowAsync(exception);
        }
    }

    private async Task StartChatAsync(Person person)
    {
        try
        {
            var chat = await Screens.Session.StartDirectAsync(_list!, person);
            await Host.ShowConversationAsync(chat);   // on a tablet, the new chat is the selected conversation
        }
        catch (Exception exception)
        {
            await ShowAsync(exception);
        }
    }

    private async Task CreateGroupAsync()
    {
        var name = _name?.Text?.Trim();
        if (string.IsNullOrEmpty(name) || _chosen.Count == 0)
        {
            return;
        }

        try
        {
            var group = await Screens.Session.CreateGroupAsync(_list!, name, [.. _found.Where(p => _chosen.Contains(p.Id))]);
            await Host.ShowConversationAsync(group);
        }
        catch (Exception exception)
        {
            await ShowAsync(exception);
        }
    }

    private static View Row()
    {
        var name = new Label { FontSize = 17 };
        var email = new Label { FontSize = 13, TextColor = Colors.Gray };
        var row = new VerticalStackLayout { Padding = new Thickness(16, 8), Children = { name, email } };
        row.BindingContextChanged += (_, _) =>
        {
            if (row.BindingContext is Person p)
            {
                (name.Text, email.Text) = (p.DisplayName, p.Email);
            }
        };
        return row;
    }
}
