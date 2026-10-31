using Alcifra.Messenger.Client.Display;
using Alcifra.Messenger.Client.Api;

namespace Alcifra.Messenger.Views;

/// <summary>
/// A group's members (core #1810): administrators first, each tap offering what the caller may do — make or revoke
/// administrator, remove, or for oneself leave — and, for an administrator, adding someone. What adding and removing
/// mean for reading is said plainly before it happens. A page of its own on a phone; on a tablet it takes the right pane
/// in place of the group's thread, the conversations staying beside it.
/// </summary>
internal sealed class MembersView : ScreenView
{
    private readonly CollectionView _list;
    private readonly ScreenAction _add;
    private Conversation _group;

    public MembersView(MessengerScreens screens, IScreenHost host, Conversation group)
        : base(screens, host)
    {
        _group = group;
        Title = ViewText.Get("Members");
        _add = new ScreenAction(ViewText.Get("AddMember"), () => Host.OpenAsync(Screens.AddMember(_group)));
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

    public override Task AppearingAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var group = await Screens.Session.Api.RefreshAsync(_group);
            if (IsClosed)
            {
                return;
            }

            _group = group;
            _list.ItemsSource = MemberRows.Build(_group, Screens.Session.Me);
            var offersAdd = _group.Offers("add-member");
            if (offersAdd != Actions.Contains(_add))
            {
                Actions = offersAdd ? [_add] : [];
            }
        }
        catch (Exception exception)
        {
            await ShowAsync(exception);
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

        var chosen = await Host.Page.DisplayActionSheetAsync(row.Name, ViewText.Get("Cancel"), null, [.. actions]);
        try
        {
            if (chosen == ViewText.Get("MakeAdmin"))
            {
                await Screens.Session.Api.MakeAdminAsync(row.Member);
            }
            else if (chosen == ViewText.Get("RevokeAdmin"))
            {
                await Screens.Session.Api.RevokeAdminAsync(row.Member);
            }
            else if (chosen == ViewText.Get("Remove")
                && await Host.Page.DisplayAlertAsync(ViewText.Get("Remove"), ViewText.Get("RemoveConfirm", row.Name), ViewText.Get("Yes"), ViewText.Get("No")))
            {
                // The next key is made here, on this administrator's device, for those who remain.
                await Screens.Session.RemoveAsync(_group, row.Member);
            }
            else if (chosen == ViewText.Get("Leave")
                && await Host.Page.DisplayAlertAsync(ViewText.Get("Leave"), ViewText.Get("LeaveConfirm"), ViewText.Get("Yes"), ViewText.Get("No")))
            {
                await Screens.Session.LeaveAsync(_group);
                // Out of the group: back past its thread, which this member can no longer open.
                await Host.LeftAsync();
                return;
            }
            else
            {
                return;
            }
        }
        catch (Exception exception)
        {
            await ShowAsync(exception);
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
