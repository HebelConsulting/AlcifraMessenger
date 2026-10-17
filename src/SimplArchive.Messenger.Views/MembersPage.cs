using SimplArchive.Messenger.Client.Display;
using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// A group's members (core #1810): administrators first, each tap offering what the caller may do — make or revoke
/// administrator, remove, or for oneself leave — and, for an administrator, adding someone. What adding and removing
/// mean for reading is said plainly before it happens.
/// </summary>
internal sealed class MembersPage : ContentPage
{
    private readonly MessengerScreens _screens;
    private readonly CollectionView _list;
    private readonly ToolbarItem _add;
    private Conversation _group;

    public MembersPage(MessengerScreens screens, Conversation group)
    {
        (_screens, _group) = (screens, group);
        Title = ViewText.Get("Members");
        _add = new ToolbarItem { Text = ViewText.Get("AddMember"), Command = new Command(async () => await Navigation.PushAsync(screens.AddMember(_group))) };
        _list = new CollectionView { SelectionMode = SelectionMode.Single, ItemTemplate = new DataTemplate(() => Row(screens.Accent)) };
        _list.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is MemberRow row)
            {
                _list.SelectedItem = null;
                await ActAsync(row);
            }
        };
        Content = _list;
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
            _group = await _screens.Session.Api.RefreshAsync(_group);
            _list.ItemsSource = MemberRows.Build(_group, _screens.Session.Me);
            var offersAdd = _group.Offers("add-member");
            if (offersAdd && !ToolbarItems.Contains(_add))
            {
                ToolbarItems.Add(_add);
            }
            else if (!offersAdd)
            {
                ToolbarItems.Remove(_add);
            }
        }
        catch (Exception exception)
        {
            await MessengerScreens.ShowAsync(this, exception);
        }
    }

    private async Task ActAsync(MemberRow row)
    {
        if (!row.HasActions)
        {
            return;
        }

        var actions = new List<string>();
        if (row.CanMakeAdmin)
        {
            actions.Add(ViewText.Get("MakeAdmin"));
        }

        if (row.CanRevokeAdmin)
        {
            actions.Add(ViewText.Get("RevokeAdmin"));
        }

        if (row.CanRemove)
        {
            actions.Add(ViewText.Get("Remove"));
        }

        if (row.CanLeave)
        {
            actions.Add(ViewText.Get("Leave"));
        }

        var chosen = await DisplayActionSheetAsync(row.Name, ViewText.Get("Cancel"), null, [.. actions]);
        try
        {
            if (chosen == ViewText.Get("MakeAdmin"))
            {
                await _screens.Session.Api.MakeAdminAsync(row.Member);
            }
            else if (chosen == ViewText.Get("RevokeAdmin"))
            {
                await _screens.Session.Api.RevokeAdminAsync(row.Member);
            }
            else if (chosen == ViewText.Get("Remove")
                && await DisplayAlertAsync(ViewText.Get("Remove"), ViewText.Get("RemoveConfirm", row.Name), ViewText.Get("Yes"), ViewText.Get("No")))
            {
                // The next key is made here, on this administrator's device, for those who remain.
                await _screens.Session.RemoveAsync(_group, row.Member);
            }
            else if (chosen == ViewText.Get("Leave")
                && await DisplayAlertAsync(ViewText.Get("Leave"), ViewText.Get("LeaveConfirm"), ViewText.Get("Yes"), ViewText.Get("No")))
            {
                await _screens.Session.LeaveAsync(_group);
                // Out of the group: back past its thread, which this member can no longer open.
                var thread = Navigation.NavigationStack.Count >= 2 ? Navigation.NavigationStack[^2] : null;
                if (thread is ThreadPage)
                {
                    Navigation.RemovePage(thread);
                }

                await Navigation.PopAsync();
                return;
            }
            else
            {
                return;
            }
        }
        catch (Exception exception)
        {
            await MessengerScreens.ShowAsync(this, exception);
        }

        await LoadAsync();
    }

    // A member's row: name, and "Administrator" beneath where it applies. Filled from its binding context, no reflection.
    private static View Row(Color accent)
    {
        var name = new Label { FontSize = 17 };
        var role = new Label { FontSize = 13, TextColor = accent, Text = ViewText.Get("Admin") };
        var row = new VerticalStackLayout { Padding = new Thickness(16, 10), Children = { name, role } };
        row.BindingContextChanged += (_, _) =>
        {
            if (row.BindingContext is MemberRow r)
            {
                name.Text = r.Name;
                role.IsVisible = r.IsAdmin;
            }
        };
        return row;
    }
}
