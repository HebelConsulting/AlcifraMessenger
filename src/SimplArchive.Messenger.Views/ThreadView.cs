using SimplArchive.Messenger.Client.Display;
using SimplArchive.Messenger.Client;
using SimplArchive.Messenger.Client.Api;
using Microsoft.Maui.Controls.Shapes;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// One conversation: bubbles oldest first, a reply's quote inside its bubble, and the compose bar. Tapping a bubble offers
/// what may be done with it; opening the thread marks it read. Beneath the messages, who is typing (#26). A page of its
/// own on a phone, the right pane on a tablet.
/// </summary>
internal sealed class ThreadView : ScreenView
{
    private readonly CollectionView _list;
    private readonly Entry _compose;
    private readonly Button _send;
    private readonly Button _attach;
    private readonly KeyPrompt _keyPrompt = new();  // a cancelled key prompt is not asked again until the caller asks
    private readonly SendGate _sending = new();   // one send at a time: a tap while one is on its way sends nothing
    private IReadOnlyList<ThreadRow> _rows = [];
    private readonly Label _replyingTo;
    private readonly Grid _replyBar;
    private readonly Label _typingLine;
    private readonly TypingSignal _typingSignal = new();   // when the compose field says "I am typing" and "I stopped"
    private readonly TypingIndicator _typers;              // who else types here, each forgotten soon after their last event
    private IDisposable? _typingEvents;
    private IDispatcherTimer? _typingTimer;
    private Conversation _conversation;
    private MessagePage? _page;
    private ThreadRow? _replyTo;

    public ThreadView(MessengerScreens screens, IScreenHost host, Conversation conversation)
        : base(screens, host)
    {
        _conversation = conversation;
        _typers = new TypingIndicator(screens.Session.Me);
        Title = ThreadRows.Title(conversation);
        List<ScreenAction> actions = [new(ViewText.Get("Refresh"), LoadAsync)];
        if (!conversation.IsDirect && !conversation.IsReadOnly)
        {
            // A group's members, and what may be done with them (core #1810); leaving is there too, and here for speed.
            actions.Add(new(ViewText.Get("Members"), () => Host.OpenAsync(Screens.Members(_conversation))));
            actions.Add(new(ViewText.Get("Leave"), LeaveAsync, Secondary: true));
        }

        Actions = actions;

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
        // "Is typing" (#26): a keystroke may start or refresh it, leaving the field stops it.
        _compose.TextChanged += (_, e) => SignalTyping(_typingSignal.Typed(DateTimeOffset.UtcNow, !string.IsNullOrWhiteSpace(e.NewTextValue)));
        _compose.Unfocused += (_, _) => SignalTyping(_typingSignal.Stopped());
        _compose.Completed += async (_, _) => await SendAsync();   // the keyboard's Send key: the same send, the same gate
        var send = _send = new Button { Text = ViewText.Get("Send"), BackgroundColor = screens.Accent, TextColor = Colors.White };
        send.Clicked += async (_, _) => await SendAsync();
        _sending.Changed += () => Dispatcher.Dispatch(ShowSending);
        var attach = _attach = new Button { Text = "+", FontSize = 22, BackgroundColor = Colors.Transparent, TextColor = screens.Accent, Padding = 0, WidthRequest = 44 };
        SemanticProperties.SetDescription(attach, ViewText.Get("Attach"));
        attach.Clicked += async (_, _) => await AttachAsync();
        var composeBar = new Grid { Padding = new Thickness(8, 6), ColumnSpacing = 8, ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        composeBar.Add(attach, 0);
        composeBar.Add(_compose, 1);
        composeBar.Add(send, 2);

        _typingLine = new Label
        {
            FontSize = 12, FontAttributes = FontAttributes.Italic, TextColor = Colors.Gray, Padding = new Thickness(16, 2),
            LineBreakMode = LineBreakMode.TailTruncation, IsVisible = false,
        };

        var page = new Grid { RowDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)] };
        page.Add(PullToRefresh.Around(_list, LoadAsync), 0, 0);   // as its Refresh does
        page.Add(_typingLine, 0, 1);   // under the thread, above the compose bar: who is typing
        page.Add(_replyBar, 0, 2);
        // Restored history (core #1813) is read, not continued: where the compose bar would be, it says so.
        page.Add(conversation.IsReadOnly ? HistoryBar(conversation) : composeBar, 0, 3);
        Content = page;
    }

    /// <summary>The conversation shown: what the list marks while it is in the pane.</summary>
    public override Conversation Subject => _conversation;

    // Shown again — also when the key dialog closes over it: asks for the key only if the prompt was not cancelled.
    public override Task AppearingAsync() => LoadAsync(ReadReason.Shown);

    // An arrival in this conversation (or one the host cannot place) reads it again, as its pull-down does (#26).
    protected override (Guid Conversation, Func<Task> Read)? ReloadsOnArrival => (_conversation.Id, () => LoadAsync(ReadReason.Arrival, quiet: true));

    // On show, the thread hears "is typing" for its conversation off the host's live connection and ticks once a second:
    // the caller's idle stop, and the typers' expiry. Off show, it stops listening, tells the others the caller stopped,
    // and forgets who typed — what it hears next time starts afresh.
    protected override void Showing(bool showing)
    {
        if (showing)
        {
            _typingEvents ??= Screens.Live.Subscribe(HeardLive);
            if (_typingTimer is null)
            {
                _typingTimer = Dispatcher.CreateTimer();
                _typingTimer.Interval = TimeSpan.FromSeconds(1);
                _typingTimer.Tick += (_, _) =>
                {
                    var now = DateTimeOffset.UtcNow;
                    SignalTyping(_typingSignal.Tick(now));
                    if (_typers.Expire(now))
                    {
                        ShowTypers();
                    }
                };
                _typingTimer.Start();
            }

            return;
        }

        _typingEvents?.Dispose();
        _typingEvents = null;
        _typingTimer?.Stop();
        _typingTimer = null;
        SignalTyping(_typingSignal.Stopped());
        _typers.Clear();
        ShowTypers();
    }

    // Off the connection's thread: only this conversation's typing, drawn on the UI thread.
    private void HeardLive(string name, string payload)
    {
        if (TypingEvent.Parse(name, payload) is not { } heard || heard.Conversation != _conversation.Id)
        {
            return;
        }

        Dispatcher.Dispatch(() =>
        {
            if (!IsClosed && TypingRule.Shows(_conversation) && _typers.Received(heard.User, heard.Typing, DateTimeOffset.UtcNow))
            {
                ShowTypers();
            }
        });
    }

    // "Anna is typing…" beneath the messages, or nothing; nothing at all where the caller's indicator is off (reciprocal).
    private void ShowTypers()
    {
        if (!TypingRule.Shows(_conversation))
        {
            _typers.Clear();
        }

        var line = _typers.Line(_conversation);
        _typingLine.Text = line;
        _typingLine.IsVisible = line is not null;
    }

    // Tells the others, best effort: a typing state lost on the way is worth nothing a few seconds later anyway.
    private void SignalTyping(TypingSend send)
    {
        if (send == TypingSend.Nothing || !TypingRule.Sends(_conversation))
        {
            return;
        }

        var conversation = _conversation;
        _ = Task.Run(async () =>
        {
            try
            {
                await Screens.Session.Api.TypingAsync(conversation, send == TypingSend.Start);
            }
            catch (Exception)
            {
                // Nobody asked to be told it failed: the next keystroke or refresh says it again.
            }
        });
    }

    // Restored history says so; where every member is here it offers to continue (core #1813), in place of the compose bar.
    private View HistoryBar(Conversation conversation)
    {
        var bar = new VerticalStackLayout { Padding = new Thickness(16, 10), Spacing = 8 };
        bar.Add(new Label
        {
            Text = conversation.Left ? ViewText.Get("LeftReadOnly") : ViewText.Get("RestoredReadOnly", conversation.RestoredFrom ?? string.Empty),
            FontSize = 13, TextColor = Colors.Gray, HorizontalTextAlignment = TextAlignment.Center,
        });
        if (conversation.Offers("continuation"))
        {
            var resume = new Button { Text = ViewText.Get("ContinueHere"), BackgroundColor = Screens.Accent, TextColor = Colors.White };
            resume.Clicked += async (_, _) => await ContinueAsync();
            bar.Add(resume);
        }

        return bar;
    }

    // The live conversation replaces this thread: the history now sits above it there.
    private async Task ContinueAsync()
    {
        try
        {
            var live = await Screens.Session.ContinueAsync(_conversation);
            await Host.ShowConversationAsync(live);
        }
        catch (ContinuationBlockedException blocked)
        {
            await Host.Page.DisplayAlertAsync(ViewText.Get("ContinueHere"), ViewText.Get("ContinueMissing", string.Join(", ", blocked.Missing)), ViewText.Get("OK"));
        }
        catch (Exception exception)
        {
            await ShowAsync(exception);
        }
    }

    // The caller's own read (Refresh, a pull, a tap on a sealed message, after sending): it may ask for the key again.
    private Task LoadAsync() => LoadAsync(ReadReason.Asked);

    // quiet: nobody asked for this read (an arrival), so a failure goes to the caller rather than in front of anyone.
    // A cancelled key prompt is a decision (KeyPrompt): the messages show sealed, no alert, and nothing asks again until
    // the caller does.
    private async Task LoadAsync(ReadReason reason, bool quiet = false)
    {
        var cancelled = false;
        var began = false;
        var again = false;
        try
        {
            var session = Screens.Session;
            var mode = _keyPrompt.Begin(reason);
            if (mode == KeyAsk.Skip)
            {
                return;   // a read waiting for the key shows the thread when it is done
            }

            began = mode == KeyAsk.Ask;
            var ask = began;
            async Task<OpenedMessage> Open(Conversation conversation, Message message)
            {
                try
                {
                    return await session.OpenAsync(conversation, message, ask);   // the first one asks for the key, once
                }
                catch (OperationCanceledException) when (ask)
                {
                    cancelled = true;
                    ask = false;
                    return await session.OpenAsync(conversation, message, askForKey: false);
                }
            }

            var conversation = await session.Api.RefreshAsync(_conversation);
            if (IsClosed)
            {
                return;   // let go of while it was read: nothing of it shows, nothing is marked read
            }

            _conversation = conversation;
            ShowTypers();   // the caller may have switched the indicator off meanwhile: then nobody's typing shows
            var (first, messages) = await session.MessagesAsync(_conversation);
            _page = first;
            var opened = new List<OpenedMessage>();
            foreach (var message in messages)
            {
                opened.Add(await Open(_conversation, message));
            }

            // Restored history this conversation continues comes first, each opened under the ids it was sealed with.
            var rows = new List<ThreadRow>();
            if (_conversation.Offers("earlier"))
            {
                foreach (var earlier in (await session.Api.EarlierAsync(_conversation)).Items)
                {
                    var (_, old) = await session.MessagesAsync(earlier);
                    var openedOld = new List<OpenedMessage>();
                    foreach (var message in old)
                    {
                        openedOld.Add(await Open(earlier, message));
                    }

                    rows.AddRange(ThreadRows.Build(earlier, openedOld, session.Me));
                }
            }

            rows.AddRange(ThreadRows.Build(_conversation, opened, session.Me));
            if (IsClosed)
            {
                return;
            }

            _list.ItemsSource = _rows = rows;
            if (opened.Count > 0 && opened[^1].State != MessageState.Locked)   // a sealed message is not read
            {
                await session.Api.MarkReadAsync(_conversation, messages[^1].SentAt);
                await Host.ConversationsChangedAsync();   // its unread count is gone from a list beside it
            }
        }
        catch (Exception exception) when (!quiet)
        {
            await ShowAsync(exception);
        }
        finally
        {
            again = began && _keyPrompt.End(cancelled);
        }

        if (again && !IsClosed)
        {
            await LoadAsync(ReadReason.Shown, quiet: true);   // something wanted a read while this one waited for the key
        }
    }

    // One send at a time (SendGate): from the tap until the server answers, Send and the keyboard's key do nothing, the
    // text stays in the field, read-only, and the thread shows it as "sending…". Answered, the field empties; refused or
    // failed, the text stays there to be sent again — never sent twice by itself.
    private async Task SendAsync()
    {
        var text = _compose.Text?.Trim();
        if (string.IsNullOrEmpty(text) || _page is null || _sending.Sending)
        {
            return;
        }

        SignalTyping(_typingSignal.Stopped());   // what was typed is on its way: no longer typing
        var sent = false;
        try
        {
            sent = await _sending.SendAsync(async () =>
            {
                _list.ItemsSource = _rows.Append(ThreadRows.Sending(text, Screens.Session.Me)).ToList();
                await Screens.Session.SendAsync(_conversation, _page, text, _replyTo?.Message.Id);
                _compose.Text = string.Empty;
                Reply(null);
            });
        }
        catch (OperationCanceledException)
        {
            _list.ItemsSource = _rows;   // the key prompt was cancelled: a decision, not a failure, and not asked again
            _keyPrompt.Decline();
            return;
        }
        catch (Exception exception)
        {
            _list.ItemsSource = _rows;   // not sent: the bubble on its way goes, the text is still in the field
            await ShowAsync(exception);
            return;
        }

        if (sent)
        {
            await LoadAsync();
        }
    }

    // While a message is on its way: Send and the attachment button disabled, the field read-only.
    private void ShowSending()
    {
        var sending = _sending.Sending;
        _send.IsEnabled = _attach.IsEnabled = !sending;
        _compose.IsReadOnly = sending;
    }

    // A photo or a file, sealed to every member and sent with whatever is typed as its caption (core #1809).
    private async Task AttachAsync()
    {
        if (_page is null || _sending.Sending)
        {
            return;
        }

        var chosen = await Host.Page.DisplayActionSheetAsync(ViewText.Get("Attach"), ViewText.Get("Cancel"), null,
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
                await Host.Page.DisplayAlertAsync(ViewText.Get("Failed"), ViewText.Get("FileTooLarge"), ViewText.Get("OK"));
                return;
            }

            var caption = _compose.Text?.Trim() ?? string.Empty;
            SignalTyping(_typingSignal.Stopped());
            if (!await _sending.SendAsync(async () =>
            {
                await Screens.Session.SendFileAsync(_conversation, _page, content, file.FileName,
                    file.ContentType ?? "application/octet-stream", caption);
                _compose.Text = string.Empty;
            }))
            {
                return;   // a message went meanwhile: this one is not sent behind its back
            }
        }
        catch (OperationCanceledException)
        {
            _keyPrompt.Decline();   // the picker or the key prompt was cancelled: nothing asks again by itself
            return;
        }
        catch (Exception exception)
        {
            await ShowAsync(exception);
        }

        await LoadAsync();
    }

    // The server takes 100 MB; sealing adds a little, so the phone stops short of it.
    private const long MaxFileBytes = 95L * 1024 * 1024;

    // Open opens it, with the key (one use per file), in the app wherever the app can show it (#27): a photo on its page, a
    // PDF or a plain text in the host's preview, a video or a sound in the player, from memory. What the app cannot show
    // gets a page that says so. Handing on is only ever the page's menu, after a warning, and only where the tenant lets
    // files leave the app.
    private async Task OpenFileAsync(ThreadRow row)
    {
        try
        {
            var content = await Screens.Session.OpenFileAsync(row.Message);
            var name = row.File!.Name;
            var opening = FileOpening.Of(content, name, row.File.ContentType, _conversation.FilesStayInApp);
            var page = opening.ShownIn switch
            {
                FileShownIn.Photo => new PhotoPage(content, name),
                FileShownIn.Preview => Screens.Preview.Open(content, name, opening.ContentType),
                FileShownIn.Player => new PlayerPage(content, name, opening.ContentType),
                _ => new UnshownFilePage(opening, name),
            };
            FileMenu.Offer(page, Screens.Handoff, opening, content, name);
            await Host.Page.Navigation.PushAsync(page);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ShowAsync(exception);
        }
    }

    private async Task ActAsync(ThreadRow row)
    {
        if (row.IsSending)
        {
            return;   // on its way: nothing to do with it until the server has it
        }

        if (row.IsLocked)
        {
            await LoadAsync(ReadReason.Asked);   // sealed because the prompt was cancelled: the tap asks once
            return;
        }

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

        var chosen = await Host.Page.DisplayActionSheetAsync(null, ViewText.Get("Cancel"), null, [.. actions]);
        try
        {
            if (chosen == ViewText.Get("Open"))
            {
                await OpenFileAsync(row);
                return;
            }

            if (chosen == ViewText.Get("WhoRead"))
            {
                await Host.Page.DisplayAlertAsync(row.Receipt, string.Join("\n", row.ReadBy), ViewText.Get("OK"));
                return;
            }

            if (chosen == ViewText.Get("Reply"))
            {
                Reply(row);
                return;
            }

            if (chosen == ViewText.Get("Edit")
                && await Host.Page.DisplayPromptAsync(ViewText.Get("Edit"), null, ViewText.Get("OK"), ViewText.Get("Cancel"), initialValue: row.EditableText) is { Length: > 0 } edited)
            {
                await Screens.Session.EditAsync(_conversation, row.Message, edited);
            }
            else if (chosen == ViewText.Get("DeleteForEveryone"))
            {
                await Screens.Session.Api.DeleteForEveryoneAsync(row.Message);
            }
            else if (chosen == ViewText.Get("DeleteForMe"))
            {
                await Screens.Session.Api.HideForMeAsync(row.Message);
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

    private async Task LeaveAsync()
    {
        if (!await Host.Page.DisplayAlertAsync(ViewText.Get("Leave"), ViewText.Get("LeaveConfirm"), ViewText.Get("Yes"), ViewText.Get("No")))
        {
            return;
        }

        try
        {
            await Screens.Session.LeaveAsync(_conversation);
            await Host.LeftAsync();
        }
        catch (Exception exception)
        {
            await ShowAsync(exception);
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
