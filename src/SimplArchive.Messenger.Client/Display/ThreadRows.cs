using System.Globalization;
using SimplArchive.Messenger.Client;
using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Client.Display;

/// <summary>One bubble as the thread draws it: whose, what it says, what it quotes, and what may be done with it.</summary>
public sealed record ThreadRow(
    Message Message, bool IsMine, string? SenderName, string Text, bool IsPlaceholder, string? Quote, string Footer,
    bool CanReply, bool CanEdit, bool CanDeleteForEveryone)
{
    /// <summary>The file the message carries, opened on a tap; null for text alone.</summary>
    public SentFile? File { get; init; }

    /// <summary>The opened text, for an edit to start from; null when there is none to edit.</summary>
    public string? EditableText { get; init; }

    /// <summary>
    /// "Read" (one-to-one) or "Read by 2 of 4" (group) beneath the caller's latest message somebody has read; null on
    /// every other bubble.
    /// </summary>
    public string? Receipt { get; init; }

    /// <summary>Who has read this message, by name, for the "Read by" entry a tap offers; empty where there is no receipt.</summary>
    public IReadOnlyList<string> ReadBy { get; init; } = [];

    /// <summary>A message on its way, shown until the server answers: it offers nothing to do with it yet.</summary>
    public bool IsSending { get; init; }
}

/// <summary>
/// Turns opened messages into bubbles: the arithmetic of the thread, apart from how a bubble is drawn, so it is tested
/// on any machine.
/// </summary>
public static class ThreadRows
{
    /// <summary>
    /// The caller's message while it is on its way (#26 follow-up): its text in the caller's own bubble, marked
    /// "sending…", with nothing offered on a tap until the server has it — then the thread is read again and it is an
    /// ordinary bubble.
    /// </summary>
    public static ThreadRow Sending(string text, Guid me) =>
        new(new Message { SenderUserId = me, SentAt = DateTimeOffset.Now }, IsMine: true, SenderName: null, text, IsPlaceholder: false,
            Quote: null, ViewText.Get("Sending"), CanReply: false, CanEdit: false, CanDeleteForEveryone: false)
        {
            IsSending = true,
        };

    /// <summary>The bubbles of <paramref name="conversation"/>, oldest first.</summary>
    public static IReadOnlyList<ThreadRow> Build(Conversation conversation, IReadOnlyList<OpenedMessage> opened, Guid me)
    {
        // In a restored conversation the caller's own old messages carry their id where they were sent.
        me = conversation.MyUserId == Guid.Empty ? me : conversation.MyUserId;
        var byId = opened.ToDictionary(o => o.Message.Id);
        var names = conversation.Members.ToDictionary(m => m.UserId, m => m.DisplayName);
        var receiptOn = ReceiptMessage(conversation, opened, me);
        var rows = opened.Select(o =>
        {
            var mine = o.Message.SenderUserId == me;
            var (text, placeholder) = o.State switch
            {
                MessageState.Readable => (o.Text ?? string.Empty, false),
                MessageState.Deleted => (ViewText.Get("Deleted"), true),
                _ => (ViewText.Get("CannotOpen"), true),
            };
            var quote = o.Message.ReplyToId is { } replied && byId.TryGetValue(replied, out var original)
                ? $"{NameOf(original.Message.SenderUserId, names, me)}: {Excerpt(original)}"
                : null;
            var time = o.Message.SentAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture);
            var footer = o.Message.EditedAt is not null && o.State == MessageState.Readable ? $"{time} · {ViewText.Get("Edited")}" : time;
            footer = o.Message.Restored ? $"{footer} · {ViewText.Get("RestoredMark")}" : footer;   // as trustworthy as its restorer
            var readable = o.State == MessageState.Readable;
            return new ThreadRow(
                o.Message, mine,
                conversation.IsDirect || mine ? null : NameOf(o.Message.SenderUserId, names, me),
                text, placeholder, quote, footer,
                CanReply: readable && !conversation.IsReadOnly,
                // A message carrying a file is not edited: its text is sealed together with the file's name and type.
                CanEdit: mine && readable && o.File is null && o.Message.Offers("edit"),
                CanDeleteForEveryone: mine && o.State != MessageState.Deleted && o.Message.Offers("delete"))
            {
                EditableText = mine && readable && o.File is null ? o.Text : null,
                File = readable ? o.File : null,
            };
        });
        return [.. rows.Select(r => r.Message.Id == receiptOn?.Id ? WithReceipt(r, conversation, me, names) : r)];
    }

    // The caller's latest message, not deleted, that at least one other member has read: the one bubble the receipt
    // line sits beneath. Read positions are times, so "read" means read up to at least the moment it was sent.
    private static Message? ReceiptMessage(Conversation conversation, IReadOnlyList<OpenedMessage> opened, Guid me)
    {
        var latestRead = conversation.Members.Where(m => m.UserId != me).Select(m => m.ReadUpTo).Max();
        return latestRead is not { } upTo
            ? null
            : opened.Where(o => o.Message.SenderUserId == me && o.State != MessageState.Deleted && o.Message.SentAt <= upTo)
                .Select(o => o.Message).LastOrDefault();
    }

    private static ThreadRow WithReceipt(ThreadRow row, Conversation conversation, Guid me, Dictionary<Guid, string> names)
    {
        var others = conversation.Members.Where(m => m.UserId != me).ToList();
        var readers = others.Where(m => m.ReadUpTo >= row.Message.SentAt).ToList();
        return row with
        {
            Receipt = conversation.IsDirect ? ViewText.Get("Read") : ViewText.Get("ReadBy", readers.Count, others.Count),
            ReadBy = conversation.IsDirect ? [] : [.. readers.Select(m => NameOf(m.UserId, names, me))],
        };
    }

    /// <summary>The conversation's title: a group's name, or the other person's in a one-to-one chat.</summary>
    public static string Title(Conversation conversation) =>
        conversation.IsDirect ? conversation.CounterpartName ?? string.Empty : conversation.Name;

    private static string NameOf(Guid user, Dictionary<Guid, string> names, Guid me) =>
        user == me ? ViewText.Get("You") : names.GetValueOrDefault(user) is { Length: > 0 } name ? name : "?";

    private static string Excerpt(OpenedMessage message) => message.State switch
    {
        MessageState.Readable when message.File is { } file && string.IsNullOrEmpty(message.Text) => $"📎 {file.Name}",
        MessageState.Readable when message.Text is { } text => text.Length <= 60 ? text : $"{text[..57]}…",
        MessageState.Deleted => ViewText.Get("Deleted"),
        _ => ViewText.Get("CannotOpen"),
    };
}
