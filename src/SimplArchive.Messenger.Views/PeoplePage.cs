using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// Choosing who to talk to: one person for a chat, or a name and several people for a group. Only people with an enrolled
/// key are offered, and the page says why.
/// </summary>
internal sealed class PeoplePage : ContentPage
{
    private readonly MessengerScreens _screens;
    private readonly ConversationList _list;
    private readonly bool _group;
    private readonly CollectionView _people;
    private readonly Entry? _name;
    private readonly HashSet<Guid> _chosen = [];
    private IReadOnlyList<Person> _found = [];

    public PeoplePage(MessengerScreens screens, ConversationList list, bool group)
    {
        (_screens, _list, _group) = (screens, list, group);
        Title = ViewText.Get(group ? "NewGroup" : "NewChat");
        if (group)
        {
            ToolbarItems.Add(new ToolbarItem { Text = ViewText.Get("Create"), Command = new Command(async () => await CreateGroupAsync()) });
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
            SelectionMode = group ? SelectionMode.Multiple : SelectionMode.Single,
            ItemTemplate = new DataTemplate(Row),
        };
        _people.SelectionChanged += async (_, e) =>
        {
            if (_group)
            {
                _chosen.Clear();
                _chosen.UnionWith(e.CurrentSelection.OfType<Person>().Select(p => p.Id));
            }
            else if (e.CurrentSelection.FirstOrDefault() is Person person)
            {
                await StartChatAsync(person);
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

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await SearchAsync(string.Empty);
    }

    private async Task SearchAsync(string term)
    {
        try
        {
            _found = [.. (await _screens.Session.Api.PeopleAsync(term)).Items.Where(p => p.Id != _screens.Session.Me)];
            _people.ItemsSource = _found;
        }
        catch (Exception exception)
        {
            await MessengerScreens.ShowAsync(this, exception);
        }
    }

    private async Task StartChatAsync(Person person)
    {
        try
        {
            var chat = await _screens.Session.StartDirectAsync(_list, person);
            Navigation.InsertPageBefore(_screens.Thread(chat), this);
            await Navigation.PopAsync();
        }
        catch (Exception exception)
        {
            await MessengerScreens.ShowAsync(this, exception);
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
            var group = await _screens.Session.CreateGroupAsync(_list, name, [.. _found.Where(p => _chosen.Contains(p.Id))]);
            Navigation.InsertPageBefore(_screens.Thread(group), this);
            await Navigation.PopAsync();
        }
        catch (Exception exception)
        {
            await MessengerScreens.ShowAsync(this, exception);
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
