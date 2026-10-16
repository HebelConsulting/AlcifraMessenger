using System.Globalization;
using SimplArchive.Messenger.Client;
using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Views;

/// <summary>One bubble as the thread draws it: whose, what it says, what it quotes, and what may be done with it.</summary>
public sealed record ThreadRow(
    Message Message, bool IsMine, string? SenderName, string Text, bool IsPlaceholder, string? Quote, string Footer,
    bool CanReply, bool CanEdit, bool CanDeleteForEveryone)
{
    /// <summary>The opened text, for an edit to start from; null when there is none to edit.</summary>
    public string? EditableText { get; init; }
}

/// <summary>
/// Turns opened messages into bubbles: the arithmetic of the thread, apart from how a bubble is drawn, so it is tested
/// on any machine.
/// </summary>
public static class ThreadRows
{
    /// <summary>The bubbles of <paramref name="conversation"/>, oldest first.</summary>
    public static IReadOnlyList<ThreadRow> Build(Conversation conversation, IReadOnlyList<OpenedMessage> opened, Guid me)
    {
        var byId = opened.ToDictionary(o => o.Message.Id);
        var names = conversation.Members.ToDictionary(m => m.UserId, m => m.DisplayName);
        return [.. opened.Select(o =>
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
            var readable = o.State == MessageState.Readable;
            return new ThreadRow(
                o.Message, mine,
                conversation.IsDirect || mine ? null : NameOf(o.Message.SenderUserId, names, me),
                text, placeholder, quote, footer,
                CanReply: readable,
                CanEdit: mine && readable && o.Message.Offers("edit"),
                CanDeleteForEveryone: mine && o.State != MessageState.Deleted && o.Message.Offers("delete"))
            {
                EditableText = mine && readable ? o.Text : null,
            };
        })];
    }

    /// <summary>The conversation's title: a group's name, or the other person's in a one-to-one chat.</summary>
    public static string Title(Conversation conversation) =>
        conversation.IsDirect ? conversation.CounterpartName ?? string.Empty : conversation.Name;

    private static string NameOf(Guid user, Dictionary<Guid, string> names, Guid me) =>
        user == me ? ViewText.Get("You") : names.GetValueOrDefault(user) is { Length: > 0 } name ? name : "?";

    private static string Excerpt(OpenedMessage message) => message.State switch
    {
        MessageState.Readable when message.Text is { } text => text.Length <= 60 ? text : $"{text[..57]}…",
        MessageState.Deleted => ViewText.Get("Deleted"),
        _ => ViewText.Get("CannotOpen"),
    };
}
