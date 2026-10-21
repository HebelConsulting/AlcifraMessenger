using SimplArchive.Messenger.Client.Display;
using System.Globalization;
using SimplArchive.Messenger.Client;
using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Client.Tests;

public sealed class DisplayTests
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
                Assert.False(string.IsNullOrWhiteSpace(ViewText.Get(key, 3, 4)), $"{key} is empty in {language}");
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

    [Fact]
    public void Members_list_administrators_first_and_offer_only_what_the_links_allow()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        var eve = Guid.NewGuid();
        Member Row(Guid id, string name, bool admin, params string[] rels) => new()
        {
            UserId = id, DisplayName = name, IsAdmin = admin,
            Links = [.. rels.Select(r => new Link(r, $"/x/{r}", "PUT"))],
        };
        var group = new Conversation
        {
            Name = "Team",
            Members = [Row(eve, "Eve", false, "remove", "make-admin"), Row(Tom, "Tom", true, "remove", "revoke-admin"), Row(Me, "Anna", true, "remove", "revoke-admin")],
        };

        var rows = MemberRows.Build(group, Me);

        Assert.Equal(["You", "Tom", "Eve"], rows.Select(r => r.Name));   // administrators first, the caller first among them
        var me = rows[0];
        Assert.True(me.CanLeave && !me.CanRemove);                   // one's own row leaves, never "removes"
        Assert.True(rows[1].CanRevokeAdmin && rows[1].CanRemove && !rows[1].CanMakeAdmin);
        Assert.True(rows[2].CanMakeAdmin && !rows[2].IsAdmin);

        // A member who is not an administrator sees no links on others, so taps offer nothing.
        var plain = MemberRows.Build(new Conversation { Members = [Row(Tom, "Tom", true), Row(Me, "Anna", false, "remove")] }, Me);
        Assert.False(plain.Single(r => !r.IsMe).HasActions);
        Assert.True(plain.Single(r => r.IsMe).CanLeave);
    }

    [Fact]
    public void A_file_shows_on_its_bubble_cannot_be_edited_and_is_quoted_by_its_name()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        var chat = new Conversation { IsDirect = true, CounterpartName = "Tom", Members = [new Member { UserId = Tom, DisplayName = "Tom" }] };
        var photo = Opened(Me, string.Empty, MessageState.Readable, links: ["edit", "delete"]) with { File = new SentFile("harbour.jpg", "image/jpeg") };
        var reply = Opened(Tom, "Nice", MessageState.Readable, replyTo: photo.Message.Id);

        var rows = ThreadRows.Build(chat, [photo, reply], Me);

        Assert.Equal(new SentFile("harbour.jpg", "image/jpeg"), rows[0].File);
        Assert.False(rows[0].CanEdit);                // its text is sealed with the file's name and type
        Assert.True(rows[0].CanDeleteForEveryone);
        Assert.Equal("You: 📎 harbour.jpg", rows[1].Quote);
    }

    [Fact]
    public void A_one_to_one_receipt_sits_beneath_my_latest_message_the_other_has_read()
    {
        var start = DateTimeOffset.UtcNow;
        var opened = new[]
        {
            At(Opened(Me, "first", MessageState.Readable), start),
            At(Opened(Tom, "reply", MessageState.Readable), start.AddMinutes(1)),
            At(Opened(Me, "second", MessageState.Readable), start.AddMinutes(2)),
            At(Opened(Me, "unread", MessageState.Readable), start.AddMinutes(4)),
        };
        var chat = new Conversation
        {
            IsDirect = true, CounterpartName = "Tom",
            Members = [new Member { UserId = Me, ReadUpTo = start.AddMinutes(1) }, new Member { UserId = Tom, DisplayName = "Tom", ReadUpTo = start.AddMinutes(3) }],
        };

        var rows = ThreadRows.Build(chat, opened, Me);

        Assert.Equal([null, null, "Read", null], rows.Select(r => r.Receipt));
        Assert.Empty(rows[2].ReadBy);   // nobody to list in a one-to-one chat
    }

    [Fact]
    public void A_group_receipt_counts_and_names_who_has_read_it()
    {
        var start = DateTimeOffset.UtcNow;
        var ina = Guid.NewGuid();
        var opened = new[] { At(Opened(Me, "hello", MessageState.Readable), start) };
        var group = new Conversation
        {
            Name = "Crew",
            Members =
            [
                new Member { UserId = Me, DisplayName = "Anna", ReadUpTo = start },
                new Member { UserId = Tom, DisplayName = "Tom", ReadUpTo = start.AddSeconds(5) },
                new Member { UserId = ina, DisplayName = "Ina" },
            ],
        };

        var row = Assert.Single(ThreadRows.Build(group, opened, Me));

        Assert.Equal("Read by 1 of 2", row.Receipt);   // the caller's own position never counts
        Assert.Equal(["Tom"], row.ReadBy);
    }

    [Fact]
    public void No_receipt_while_nobody_else_has_read_and_none_on_a_deleted_message()
    {
        var start = DateTimeOffset.UtcNow;
        var opened = new[] { At(Opened(Me, "kept", MessageState.Readable), start), At(Opened(Me, null, MessageState.Deleted), start.AddMinutes(1)) };
        Conversation Chat(DateTimeOffset? tomRead) => new()
        {
            IsDirect = true, CounterpartName = "Tom",
            Members = [new Member { UserId = Me }, new Member { UserId = Tom, DisplayName = "Tom", ReadUpTo = tomRead }],
        };

        Assert.All(ThreadRows.Build(Chat(null), opened, Me), r => Assert.Null(r.Receipt));
        Assert.Equal(["Read", null], ThreadRows.Build(Chat(start.AddMinutes(2)), opened, Me).Select(r => r.Receipt));
    }

    [Fact]
    public void A_restored_conversation_knows_my_old_messages_by_my_id_there_and_marks_them()
    {
        var before = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        try
        {
            var myOldId = Guid.NewGuid();
            var mine = Opened(myOldId, "from the old place", MessageState.Readable);
            mine.Message.Restored = true;
            var restored = new Conversation
            {
                Name = "Crew (restored)", IsReadOnly = true, Restored = true, MyUserId = myOldId,
                Members = [new Member { UserId = myOldId, DisplayName = "Anna" }, new Member { UserId = Tom, DisplayName = "Tom" }],
            };

            var row = Assert.Single(ThreadRows.Build(restored, [mine], Me));

            Assert.True(row.IsMine);                          // Anna's id there, not her id here
            Assert.EndsWith("· restored", row.Footer);
            Assert.False(row.CanReply);                       // history: nothing is sent into it
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }

    [Theory]
    [InlineData(-1, false)]      // not laid out yet: neither layout is known, so the phone's
    [InlineData(699.9, false)]
    [InlineData(700, true)]
    [InlineData(1366, true)]
    public void The_window_width_alone_decides_whether_the_conversation_shows_beside_the_list(double width, bool splits) =>
        Assert.Equal(splits, MessagesSplit.Splits(width));

    [Theory]
    [InlineData(700, 320)]       // a third would be 233: the list keeps its narrowest column
    [InlineData(1050, 350)]      // a third
    [InlineData(1366, 400)]      // a third would be 455: the list stops at its widest
    public void The_divider_is_fixed_at_about_a_third_clamped(double width, double list) =>
        Assert.Equal(list, MessagesSplit.ListWidth(width));

    [Fact]
    public void After_the_list_is_read_again_the_pane_keeps_the_fresh_row_or_nothing()
    {
        var team = Guid.NewGuid();
        var stale = new Conversation { Id = team, Name = "Team", Unread = 3 };
        var fresh = new Conversation { Id = team, Name = "Team", Unread = 0 };
        var other = new Conversation { Id = Guid.NewGuid(), Name = "Other" };

        Assert.Same(fresh, MessagesSplit.AfterReload(stale.Id, [other, fresh]));   // today's row, not the one from before
        Assert.Null(MessagesSplit.AfterReload(team, [other]));                    // left it: the pane empties
        Assert.Null(MessagesSplit.AfterReload(null, [other, fresh]));             // nothing selected stays nothing
    }

    [Fact]
    public void Back_on_a_phone_marks_the_conversation_it_came_from_for_a_moment_and_never_beside_a_pane()
    {
        var team = new Conversation { Id = Guid.NewGuid(), Name = "Team" };
        var other = new Conversation { Id = Guid.NewGuid(), Name = "Other" };

        Assert.Equal(team.Id, MessagesSplit.ReturnMark(team.Id, [other, team], splits: false));
        Assert.Null(MessagesSplit.ReturnMark(team.Id, [other, team], splits: true));   // the selection's mark, which stays
        Assert.Null(MessagesSplit.ReturnMark(team.Id, [other], splits: false));        // left it: nothing to find
        Assert.Null(MessagesSplit.ReturnMark(null, [other, team], splits: false));
        Assert.InRange(MessagesSplit.ReturnMarkFor, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));   // a moment, not a state
    }

    private static OpenedMessage At(OpenedMessage message, DateTimeOffset sentAt)
    {
        message.Message.SentAt = sentAt;
        return message;
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
