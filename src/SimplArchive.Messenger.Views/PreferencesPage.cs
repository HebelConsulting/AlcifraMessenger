using SimplArchive.Messenger.Client.Api;
using SimplArchive.Messenger.Client.Display;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// The caller's own Messenger settings (#26), from the conversation list's menu: today the typing indicator, a switch
/// that works both ways. A change is saved at once, under the tag of the read that set the switch; changed on another
/// device meanwhile, the page says so and shows what is stored now.
/// </summary>
internal sealed class PreferencesPage : ContentPage
{
    private readonly MessengerScreens _screens;
    private readonly ConversationList _list;
    private readonly Switch _typing;
    private MessengerPreferences? _read;
    private bool _filling;

    public PreferencesPage(MessengerScreens screens, ConversationList list)
    {
        (_screens, _list) = (screens, list);
        Title = ViewText.Get("Preferences");
        _typing = new Switch { OnColor = screens.Accent, IsEnabled = false, VerticalOptions = LayoutOptions.Center };
        _typing.Toggled += async (_, e) => await SaveAsync(e.Value);
        var label = new Label { Text = ViewText.Get("TypingIndicator"), FontSize = 17, VerticalTextAlignment = TextAlignment.Center };
        SemanticProperties.SetDescription(_typing, ViewText.Get("TypingIndicator"));
        var row = new Grid { ColumnSpacing = 12, ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        row.Add(label, 0);
        row.Add(_typing, 1);
        Content = new VerticalStackLayout
        {
            Padding = new Thickness(16, 12),
            Spacing = 6,
            Children =
            {
                row,
                new Label { Text = ViewText.Get("TypingIndicatorHint"), FontSize = 13, TextColor = Colors.Gray },
            },
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ReadAsync();
    }

    private async Task ReadAsync()
    {
        try
        {
            Show(await _screens.Session.Api.PreferencesAsync(_list));
        }
        catch (Exception exception)
        {
            await MessengerScreens.ShowAsync(this, exception);
        }
    }

    private void Show(MessengerPreferences preferences)
    {
        _read = preferences;
        _filling = true;   // setting the switch from the server is not the caller's change
        _typing.IsToggled = preferences.TypingIndicator;
        _filling = false;
        _typing.IsEnabled = true;
    }

    private async Task SaveAsync(bool on)
    {
        if (_filling || _read is not { } read || read.TypingIndicator == on)
        {
            return;
        }

        _typing.IsEnabled = false;   // one change at a time: the next one carries this one's tag
        try
        {
            Show(await _screens.Session.Api.ReplacePreferencesAsync(read, on));
        }
        catch (MessengerApiException refusal) when (refusal.Code == "ETAG_MISMATCH")
        {
            await DisplayAlertAsync(ViewText.Get("Preferences"), ViewText.Get("PreferencesChanged"), ViewText.Get("OK"));
            await ReadAsync();
        }
        catch (Exception exception)
        {
            await MessengerScreens.ShowAsync(this, exception);
            Show(read);   // not saved: the switch shows what is stored
        }
    }
}
