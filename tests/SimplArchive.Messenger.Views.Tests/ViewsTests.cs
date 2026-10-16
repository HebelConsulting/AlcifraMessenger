using System.Globalization;
using SimplArchive.Messenger.Client;
using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Views.Tests;

public sealed class ViewsTests
{
    private static readonly Guid Me = Guid.NewGuid(), Tom = Guid.NewGuid();

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("it")]
    [InlineData("es")]
    public void Every_text_exists_in_each_language_the_apps_speak(string language)
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(language);
            foreach (var key in ViewText.Keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(ViewText.Get(key, 3)), $"{key} is empty in {language}");
            }

            // Not silently English: a translated language differs from English in at least its title.
            if (language != "en")
            {
                CultureInfo.CurrentUICulture = new CultureInfo(language);
                var translated = ViewText.Get("Messages");
                CultureInfo.CurrentUICulture = new CultureInfo("en");
                Assert.NotEqual(ViewText.Get("Messages"), translated);
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }

    [Fact]
    public void A_bubble_knows_whose_it_is_what_it_quotes_and_what_may_be_done_with_it()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");   // the footer's word is asserted in English
        var group = new Conversation
        {
            Name = "Team",
            Members = [new Member { UserId = Me, DisplayName = "Anna" }, new Member { UserId = Tom, DisplayName = "Tom" }],
        };
        var first = Opened(Tom, "Hello", MessageState.Readable);
        var reply = Opened(Me, "Hi Tom", MessageState.Readable, replyTo: first.Message.Id, edited: true, links: ["edit", "delete"]);
        var gone = Opened(Me, null, MessageState.Deleted, links: ["edit", "delete"]);
        var broken = Opened(Tom, null, MessageState.Unopenable);

        var rows = ThreadRows.Build(group, [first, reply, gone, broken], Me);

        Assert.Equal(("Tom", false, true), (rows[0].SenderName, rows[0].IsMine, rows[0].CanReply));
        Assert.False(rows[0].CanEdit);   // somebody else's
        Assert.Null(rows[1].SenderName);   // mine: no name above it
        Assert.Equal("Tom: Hello", rows[1].Quote);
        Assert.True(rows[1].CanEdit && rows[1].CanDeleteForEveryone);
        Assert.Equal("Hi Tom", rows[1].EditableText);
        Assert.Contains("edited", rows[1].Footer, StringComparison.Ordinal);
        Assert.True(rows[2].IsPlaceholder);
        Assert.False(rows[2].CanEdit || rows[2].CanDeleteForEveryone || rows[2].CanReply);
        Assert.True(rows[3].IsPlaceholder);
        Assert.False(rows[3].CanReply);
    }

    [Fact]
    public void A_one_to_one_chat_is_titled_by_the_other_person_and_shows_no_sender_names()
    {
        var chat = new Conversation { IsDirect = true, Name = "direct-x", CounterpartName = "Tom", Members = [new Member { UserId = Tom, DisplayName = "Tom" }] };
        Assert.Equal("Tom", ThreadRows.Title(chat));
        Assert.Null(Assert.Single(ThreadRows.Build(chat, [Opened(Tom, "Hi", MessageState.Readable)], Me)).SenderName);
    }

    private static OpenedMessage Opened(Guid sender, string? text, MessageState state, Guid? replyTo = null, bool edited = false, string[]? links = null) =>
        new(new Message
        {
            Id = Guid.NewGuid(),
            SenderUserId = sender,
            SentAt = DateTimeOffset.UtcNow,
            EditedAt = edited ? DateTimeOffset.UtcNow : null,
            DeletedAt = state == MessageState.Deleted ? DateTimeOffset.UtcNow : null,
            ReplyToId = replyTo,
            Links = [.. (links ?? []).Select(l => new Link(l, $"/x/{l}", "PUT"))],
        }, text, state);
}
