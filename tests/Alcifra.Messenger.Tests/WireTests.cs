using System.Security.Cryptography;
using System.Text;
using Alcifra.Messenger.Client;
using Alcifra.Messenger.Client.Api;
using SimplArchive.ModuleAbi;
using ClientKey = Alcifra.Messenger.Client.Api.WrappedKey;

namespace Alcifra.Messenger.Tests;

// The client library against the REAL controllers, in process: every field name, rel and status the phone will meet is
// the one the server sends. The archive behind them is the in-memory fake; the core's root and its problem documents
// are reproduced as the core writes them (links; errorCode).
public sealed class WireTests : IAsyncDisposable
{
    private readonly ServerRig _rig = new();
    private readonly Guid _anna, _tom;

    public WireTests() => (_anna, _tom) = (_rig.AddPerson("Anna"), _rig.AddPerson("Tom"));

    public ValueTask DisposeAsync() => _rig.DisposeAsync();

    [Fact]
    public async Task A_conversation_round_trips_from_one_phone_to_another()
    {
        var api = await _rig.StartAsync();
        _rig.Caller.UserId = _anna;
        Assert.True(await api.DiscoverAsync());

        var tom = Assert.Single((await api.PeopleAsync("tom")).Items);
        Assert.Equal("Tom", tom.DisplayName);
        Assert.Single((await api.CertificatesAsync(tom)).CertificatesPem);

        var list = await api.ConversationsAsync();
        var chat = await api.OpenDirectAsync(list, tom, [new ClientKey(_anna, [1]), new ClientKey(_tom, [2])]);
        Assert.True(chat.IsDirect);
        Assert.Equal([1], chat.MyKey);
        Assert.Equal("Tom", chat.CounterpartName);

        var key = ConversationKeys.NewKey();
        var id = Guid.NewGuid();
        var page = await api.MessagesAsync(chat);
        var sent = await api.SendAsync(page, id, chat.Epoch,
            MessageFormat.Seal(key, new MessageContext(chat.Id, chat.Epoch, id, _anna), "Hello Tom"u8));
        Assert.Equal(_tom, Assert.Single(_rig.Notices.Signals).RecipientUserId);

        _rig.Caller.UserId = _tom;
        var hisView = Assert.Single((await api.ConversationsAsync()).Items);
        Assert.Equal(1, hisView.Unread);
        var received = Assert.Single((await api.MessagesAsync(hisView)).Items);
        var text = MessageFormat.Open(key, new MessageContext(hisView.Id, received.Epoch, received.Id, received.SenderUserId), received.Ciphertext);
        Assert.Equal("Hello Tom", Encoding.UTF8.GetString(text));
        await api.MarkReadAsync(hisView, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(0, (await api.RefreshAsync(hisView)).Unread);
        Assert.Contains((_tom, hisView.Id), _rig.Notices.Cleared);

        // Anna sees how far Tom has read: the receipt beneath her message. She herself has read nothing yet.
        _rig.Caller.UserId = _anna;
        var hers = await api.RefreshAsync(chat);
        Assert.True(hers.Members.Single(m => m.UserId == _tom).ReadUpTo >= sent.SentAt);
        Assert.Null(hers.Members.Single(m => m.UserId == _anna).ReadUpTo);

        // Only the sender edits, and only under the tag they saw: a second edit with the first tag is a 412.
        _rig.Caller.UserId = _anna;
        var edited = await api.EditAsync(sent, chat.Epoch, MessageFormat.Seal(key, new MessageContext(chat.Id, chat.Epoch, id, _anna), "Hi Tom"u8));
        Assert.NotNull(edited.EditedAt);
        var stale = await Assert.ThrowsAsync<MessengerApiException>(() => api.EditAsync(sent, chat.Epoch, [1, 2, 3]));
        Assert.Equal((412, "ETAG_MISMATCH"), (stale.StatusCode, stale.Code));
    }

    [Fact]
    public async Task Typing_reaches_the_other_member_and_switching_it_off_takes_the_link_away_both_ways()
    {
        var api = await _rig.StartAsync();
        _rig.Caller.UserId = _anna;
        await api.DiscoverAsync();
        var group = await api.CreateGroupAsync(await api.ConversationsAsync(), "Team", [new ClientKey(_anna, [1]), new ClientKey(_tom, [2])]);

        // On by default: the thread offers typing, and the event goes to Tom, never back to Anna.
        Assert.True(Client.Display.TypingRule.Sends(group));
        await api.TypingAsync(group, typing: true);
        var sent = Assert.Single(_rig.Live.Sent);
        Assert.Equal([_tom], sent.RecipientUserIds);
        Assert.Equal(new Client.Display.TypingEvent(group.Id, _anna, true), Client.Display.TypingEvent.Parse(sent.Name, sent.Payload));

        // Tom switches it off, under the tag of his read: his conversations no longer offer typing, so his app neither
        // sends nor shows it, and Anna's typing is not sent to him.
        _rig.Caller.UserId = _tom;
        var list = await api.ConversationsAsync();
        var mine = await api.PreferencesAsync(list);
        Assert.True(mine.TypingIndicator);
        var off = await api.ReplacePreferencesAsync(mine, typingIndicator: false);
        Assert.False(off.TypingIndicator);
        Assert.False(Client.Display.TypingRule.Shows(Assert.Single((await api.ConversationsAsync()).Items)));
        var stale = await Assert.ThrowsAsync<MessengerApiException>(() => api.ReplacePreferencesAsync(mine, typingIndicator: true));
        Assert.Equal((412, "ETAG_MISMATCH"), (stale.StatusCode, stale.Code));

        _rig.Caller.UserId = _anna;
        await api.TypingAsync(group, typing: false);   // answered alike; nobody is left to tell
        Assert.Single(_rig.Live.Sent);
    }

    [Fact]
    public async Task Whether_files_stay_in_the_app_travels_on_every_conversation_and_only_an_explicit_yes_keeps_them()
    {
        var api = await _rig.StartAsync();
        _rig.Caller.UserId = _anna;
        await api.DiscoverAsync();
        var group = await api.CreateGroupAsync(await api.ConversationsAsync(), "Team", [new ClientKey(_anna, [1]), new ClientKey(_tom, [2])]);

        // Unset — every tenant until an administrator decides — lets a file be handed on (#27).
        Assert.False(group.FilesStayInApp);
        Assert.False(Assert.Single((await api.ConversationsAsync()).Items).FilesStayInApp);

        _rig.Archive.Settings[MessengerModule.FilesStayInAppSetting] = "true";
        Assert.True(Assert.Single((await api.ConversationsAsync()).Items).FilesStayInApp);
        Assert.True((await api.RefreshAsync(group)).FilesStayInApp);

        _rig.Archive.Settings[MessengerModule.FilesStayInAppSetting] = "false";
        Assert.False((await api.RefreshAsync(group)).FilesStayInApp);
    }

    [Fact]
    public async Task Groups_files_and_refusals_travel_as_the_server_sends_them()
    {
        var api = await _rig.StartAsync();
        _rig.Caller.UserId = _anna;
        await api.DiscoverAsync();

        var group = await api.CreateGroupAsync(await api.ConversationsAsync(), "Team", [new ClientKey(_anna, [1]), new ClientKey(_tom, [2])]);
        Assert.True(group.CanManageMembers);
        var tomAsMember = group.Members.Single(m => m.UserId == _tom);
        Assert.True(tomAsMember.Offers("make-admin"));
        Assert.Equal("Tom", tomAsMember.DisplayName);
        await api.MakeAdminAsync(tomAsMember);
        Assert.True((await api.RefreshAsync(group)).Members.Single(m => m.UserId == _tom).IsAdmin);

        var sealedFile = RandomNumberGenerator.GetBytes(64);
        var file = await api.UploadFileAsync(group, sealedFile);
        var page = await api.MessagesAsync(group);
        var id = Guid.NewGuid();
        var withFile = await api.SendAsync(page, id, group.Epoch, [9], attachmentDocumentId: file.DocumentId);
        Assert.Equal(sealedFile, await api.DownloadFileAsync(withFile));   // through the redirect, from the store

        // After a removal the new epoch's key joins the old one, so older messages still open.
        var eve = Guid.NewGuid();
        _rig.Archive.Users.Add(new ModuleUser(eve, "Eve", "eve@x.io"));
        _rig.Archive.Certificates[eve] = ["pem"];
        await api.AddMemberAsync(group, eve, [new ClientKey(_anna, [3]), new ClientKey(_tom, [3]), new ClientKey(eve, [3])]);
        group = await api.RefreshAsync(group);
        await api.RemoveMemberAsync(group.Members.Single(m => m.UserId == eve), [new ClientKey(_anna, [4]), new ClientKey(_tom, [5])]);
        group = await api.RefreshAsync(group);
        Assert.Equal([(1, (byte)1), (2, (byte)3), (3, (byte)4)], group.MyKeys.Select(k => (k.Epoch, k.Key.Single())));

        // Tom leaves with no key; Anna's phone sees the closed epoch and publishes the next.
        _rig.Caller.UserId = _tom;
        await api.LeaveAsync((await api.RefreshAsync(group)).Members.Single(m => m.UserId == _tom));
        _rig.Caller.UserId = _anna;
        group = await api.RefreshAsync(group);
        Assert.True(group.NeedsNewEpoch);
        await api.PublishEpochAsync(group, [new ClientKey(_anna, [6])]);
        group = await api.RefreshAsync(group);
        Assert.Equal((4, false), (group.Epoch, group.NeedsNewEpoch));
        page = await api.MessagesAsync(group);

        // A refusal arrives with the server's own code.
        var refusal = await Assert.ThrowsAsync<MessengerApiException>(() => api.SendAsync(page, Guid.NewGuid(), group.Epoch + 1, [9]));
        Assert.Equal((409, "MESSENGER_STALE_EPOCH"), (refusal.StatusCode, refusal.Code));
    }
}
