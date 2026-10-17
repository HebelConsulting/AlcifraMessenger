using SimplArchive.Messenger.Client;
using SimplArchive.Messenger.Client.Api;
using Microsoft.Maui.Controls.Shapes;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// One conversation: bubbles oldest first, a reply's quote inside its bubble, and the compose bar. Tapping a bubble offers
/// what may be done with it; opening the thread marks it read.
/// </summary>
internal sealed class ThreadPage : ContentPage
{
    private readonly MessengerScreens _screens;
    private readonly CollectionView _list;
    private readonly Entry _compose;
    private readonly Label _replyingTo;
    private readonly Grid _replyBar;
    private Conversation _conversation;
    private MessagePage? _page;
    private ThreadRow? _replyTo;

    public ThreadPage(MessengerScreens screens, Conversation conversation)
    {
        _screens = screens;
        _conversation = conversation;
        Title = ThreadRows.Title(conversation);
        ToolbarItems.Add(new ToolbarItem { Text = ViewText.Get("Refresh"), Command = new Command(async () => await LoadAsync()) });
        if (!conversation.IsDirect && !conversation.IsReadOnly)
        {
            // A group's members, and what may be done with them (core #1810); leaving is there too, and here for speed.
            ToolbarItems.Add(new ToolbarItem { Text = ViewText.Get("Members"), Command = new Command(async () => await Navigation.PushAsync(screens.Members(_conversation))) });
            ToolbarItems.Add(new ToolbarItem { Text = ViewText.Get("Leave"), Order = ToolbarItemOrder.Secondary, Command = new Command(async () => await LeaveAsync()) });
        }

        _list = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepLastItemInView,
            ItemTemplate = new DataTemplate(() => Bubble(screens.Accent)),
        };
        _list.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is ThreadRow row)
            {
                _list.SelectedItem = null;
                await ActAsync(row);
            }
        };

        _replyingTo = new Label { FontSize = 13, TextColor = Colors.Gray, VerticalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation };
        var cancelReply = new Button { Text = "✕", BackgroundColor = Colors.Transparent, TextColor = Colors.Gray, Padding = 0, WidthRequest = 36 };
        cancelReply.Clicked += (_, _) => Reply(null);
        _replyBar = new Grid { IsVisible = false, Padding = new Thickness(12, 4), ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        _replyBar.Add(_replyingTo, 0);
        _replyBar.Add(cancelReply, 1);

        _compose = new Entry { Placeholder = ViewText.Get("TypeMessage"), ReturnType = ReturnType.Send };
        _compose.Completed += async (_, _) => await SendAsync();
        var send = new Button { Text = ViewText.Get("Send"), BackgroundColor = screens.Accent, TextColor = Colors.White };
        send.Clicked += async (_, _) => await SendAsync();
        var attach = new Button { Text = "+", FontSize = 22, BackgroundColor = Colors.Transparent, TextColor = screens.Accent, Padding = 0, WidthRequest = 44 };
        SemanticProperties.SetDescription(attach, ViewText.Get("Attach"));
        attach.Clicked += async (_, _) => await AttachAsync();
        var composeBar = new Grid { Padding = new Thickness(8, 6), ColumnSpacing = 8, ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        composeBar.Add(attach, 0);
        composeBar.Add(_compose, 1);
        composeBar.Add(send, 2);

        var page = new Grid { RowDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)] };
        page.Add(_list, 0, 0);
        page.Add(_replyBar, 0, 1);
        // Restored history (core #1813) is read, not continued: where the compose bar would be, it says so.
        page.Add(conversation.IsReadOnly
            ? new Label
            {
                Text = ViewText.Get("RestoredReadOnly", conversation.RestoredFrom ?? string.Empty),
                FontSize = 13, TextColor = Colors.Gray, Padding = new Thickness(16, 10), HorizontalTextAlignment = TextAlignment.Center,
            }
            : composeBar, 0, 2);
        Content = page;
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
            var session = _screens.Session;
            _conversation = await session.Api.RefreshAsync(_conversation);
            var (first, messages) = await session.MessagesAsync(_conversation);
            _page = first;
            var opened = new List<OpenedMessage>();
            foreach (var message in messages)
            {
                opened.Add(await session.OpenAsync(_conversation, message));   // the first one asks for the key, once
            }

            var rows = ThreadRows.Build(_conversation, opened, session.Me);
            _list.ItemsSource = rows;
            if (messages.Count > 0)
            {
                await session.Api.MarkReadAsync(_conversation, messages[^1].SentAt);
            }
        }
        catch (Exception exception)
        {
            await MessengerScreens.ShowAsync(this, exception);
        }
    }

    private async Task SendAsync()
    {
        var text = _compose.Text?.Trim();
        if (string.IsNullOrEmpty(text) || _page is null)
        {
            return;
        }

        try
        {
            await _screens.Session.SendAsync(_conversation, _page, text, _replyTo?.Message.Id);
            _compose.Text = string.Empty;
            Reply(null);
        }
        catch (Exception exception)
        {
            await MessengerScreens.ShowAsync(this, exception);
        }

        await LoadAsync();
    }

    // A photo or a file, sealed to every member and sent with whatever is typed as its caption (core #1809).
    private async Task AttachAsync()
    {
        if (_page is null)
        {
            return;
        }

        var chosen = await DisplayActionSheetAsync(ViewText.Get("Attach"), ViewText.Get("Cancel"), null,
            ViewText.Get("TakePhoto"), ViewText.Get("ChoosePhoto"), ViewText.Get("ChooseFile"));
        try
        {
            FileResult? file = chosen == ViewText.Get("TakePhoto") ? await MediaPicker.Default.CapturePhotoAsync()
                : chosen == ViewText.Get("ChoosePhoto") ? (await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { SelectionLimit = 1 })).FirstOrDefault()
                : chosen == ViewText.Get("ChooseFile") ? await FilePicker.Default.PickAsync()
                : null;
            if (file is null)
            {
                return;
            }

            byte[] content;
            await using (var stream = await file.OpenReadAsync())
            using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer);
                content = buffer.ToArray();
            }

            if (content.LongLength > MaxFileBytes)
            {
                await DisplayAlertAsync(ViewText.Get("Failed"), ViewText.Get("FileTooLarge"), ViewText.Get("OK"));
                return;
            }

            await _screens.Session.SendFileAsync(_conversation, _page, content, file.FileName,
                file.ContentType ?? "application/octet-stream", _compose.Text?.Trim() ?? string.Empty);
            _compose.Text = string.Empty;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await MessengerScreens.ShowAsync(this, exception);
        }

        await LoadAsync();
    }

    // The server takes 100 MB; sealing adds a little, so the phone stops short of it.
    private const long MaxFileBytes = 95L * 1024 * 1024;

    // A tap opens it, with the key (one use per file): a photo in the app, anything else handed on as a sealed document is.
    private async Task OpenFileAsync(ThreadRow row)
    {
        try
        {
            var content = await _screens.Session.OpenFileAsync(row.Message);
            if (row.File!.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                await Navigation.PushAsync(new PhotoPage(_screens.Handoff, content, row.File.Name, row.File.ContentType));
            }
            else
            {
                await _screens.Handoff.OpenWithAsync(content, row.File.Name, row.File.ContentType);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await MessengerScreens.ShowAsync(this, exception);
        }
    }

    private async Task ActAsync(ThreadRow row)
    {
        var actions = new List<string>();
        if (row.File is not null)
        {
            actions.Add(ViewText.Get("Open"));
        }

        if (row.CanReply)
        {
            actions.Add(ViewText.Get("Reply"));
        }

        if (row.CanEdit)
        {
            actions.Add(ViewText.Get("Edit"));
        }

        if (row.CanDeleteForEveryone)
        {
            actions.Add(ViewText.Get("DeleteForEveryone"));
        }

        actions.Add(ViewText.Get("DeleteForMe"));
        if (row.ReadBy.Count > 0)
        {
            actions.Add(ViewText.Get("WhoRead"));
        }

        var chosen = await DisplayActionSheetAsync(null, ViewText.Get("Cancel"), null, [.. actions]);
        try
        {
            if (chosen == ViewText.Get("Open"))
            {
                await OpenFileAsync(row);
                return;
            }

            if (chosen == ViewText.Get("WhoRead"))
            {
                await DisplayAlertAsync(row.Receipt, string.Join("\n", row.ReadBy), ViewText.Get("OK"));
                return;
            }

            if (chosen == ViewText.Get("Reply"))
            {
                Reply(row);
                return;
            }

            if (chosen == ViewText.Get("Edit")
                && await DisplayPromptAsync(ViewText.Get("Edit"), null, ViewText.Get("OK"), ViewText.Get("Cancel"), initialValue: row.EditableText) is { Length: > 0 } edited)
            {
                await _screens.Session.EditAsync(_conversation, row.Message, edited);
            }
            else if (chosen == ViewText.Get("DeleteForEveryone"))
            {
                await _screens.Session.Api.DeleteForEveryoneAsync(row.Message);
            }
            else if (chosen == ViewText.Get("DeleteForMe"))
            {
                await _screens.Session.Api.HideForMeAsync(row.Message);
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

    private async Task LeaveAsync()
    {
        if (!await DisplayAlertAsync(ViewText.Get("Leave"), ViewText.Get("LeaveConfirm"), ViewText.Get("Yes"), ViewText.Get("No")))
        {
            return;
        }

        try
        {
            await _screens.Session.LeaveAsync(_conversation);
            await Navigation.PopAsync();
        }
        catch (Exception exception)
        {
            await MessengerScreens.ShowAsync(this, exception);
        }
    }

    private void Reply(ThreadRow? row)
    {
        _replyTo = row;
        _replyBar.IsVisible = row is not null;
        _replyingTo.Text = row is null ? string.Empty : ViewText.Get("ReplyingTo", row.SenderName ?? (row.IsMine ? ViewText.Get("You") : Title));
        if (row is not null)
        {
            _compose.Focus();
        }
    }

    // A bubble: mine on the right in the accent, others on the left; a group's sender above, a reply's quote inside, the
    // time (and "edited") beneath. Filled from its binding context, no reflection.
    private static View Bubble(Color accent)
    {
        var sender = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold };
        var quote = new Label { FontSize = 12, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 };
        var quoteBox = new Border { StrokeThickness = 0, Padding = new Thickness(8, 4), StrokeShape = new RoundRectangle { CornerRadius = 6 }, Content = quote };
        var text = new Label { FontSize = 16 };
        var fileChip = new Label { FontSize = 15, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.MiddleTruncation };
        var footer = new Label { FontSize = 11, HorizontalTextAlignment = TextAlignment.End };
        var receipt = new Label { FontSize = 11, TextColor = Colors.Gray, HorizontalTextAlignment = TextAlignment.End, HorizontalOptions = LayoutOptions.End };
        var body = new VerticalStackLayout { Spacing = 3, Children = { sender, quoteBox, fileChip, text, footer } };
        var bubble = new Border
        {
            StrokeThickness = 0,
            Padding = new Thickness(12, 8),
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            MaximumWidthRequest = 320,
            Content = body,
        };
        // The receipt sits beneath the bubble, outside it: it describes the others, not the message.
        var row = new VerticalStackLayout { Padding = new Thickness(10, 3), Spacing = 2, Children = { bubble, receipt } };
        row.BindingContextChanged += (_, _) =>
        {
            if (row.BindingContext is not ThreadRow r)
            {
                return;
            }

            bubble.HorizontalOptions = r.IsMine ? LayoutOptions.End : LayoutOptions.Start;
            bubble.BackgroundColor = r.IsMine ? accent : Color.FromArgb("#ECEFF1");
            var ink = r.IsMine ? Colors.White : Colors.Black;
            text.TextColor = ink;
            text.Text = r.Text;
            text.IsVisible = r.File is null || r.Text.Length > 0;
            fileChip.IsVisible = r.File is not null;
            fileChip.TextColor = ink;
            fileChip.Text = r.File is { } f ? $"📎 {f.Name}" : null;
            text.FontAttributes = r.IsPlaceholder ? FontAttributes.Italic : FontAttributes.None;
            footer.TextColor = r.IsMine ? Colors.White.WithAlpha(0.8f) : Colors.Gray;
            footer.Text = r.Footer;
            sender.IsVisible = r.SenderName is not null;
            sender.Text = r.SenderName;
            sender.TextColor = accent;
            quoteBox.IsVisible = r.Quote is not null;
            quoteBox.BackgroundColor = r.IsMine ? Colors.White.WithAlpha(0.2f) : Colors.White;
            quote.TextColor = r.IsMine ? Colors.White : Colors.DimGray;
            quote.Text = r.Quote;
            receipt.IsVisible = r.Receipt is not null;
            receipt.Text = r.Receipt;
        };
        return row;
    }
}
