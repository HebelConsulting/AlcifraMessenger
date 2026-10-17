using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SimplArchive.Messenger.ReadModel;
using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger.Tests;

// The Messenger's rules, against its real tables (SQLite) and an archive in memory: membership is the current epoch's
// key rows, a removal starts a new epoch for those who remain, only the sender changes a message, and only under the tag
// they read.
public sealed class ConversationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly MessengerReadModelContext _db;
    private readonly FakeArchive _archive = new();
    private readonly FakeCaller _caller = new(Guid.NewGuid());
    private readonly FakeNotices _notices = new();
    private readonly Conversations _conversations;
    private readonly Messages _messages;
    private readonly Files _files;
    private readonly Guid _anna = Guid.NewGuid(), _tom = Guid.NewGuid(), _eve = Guid.NewGuid(), _nokey = Guid.NewGuid();
    private readonly Guid _repository = Guid.NewGuid();

    public ConversationTests()
    {
        _connection.Open();
        _db = new MessengerReadModelContext(new DbContextOptionsBuilder<MessengerReadModelContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _archive.Settings[MessengerModule.RepositorySetting] = _repository.ToString();
        foreach (var (id, name) in new[] { (_anna, "Anna"), (_tom, "Tom"), (_eve, "Eve"), (_nokey, "Nokey") })
        {
            var email = $"{name.ToLowerInvariant()}@x.io";
            _archive.Users.Add(new ModuleUser(id, name, email));
            _caller.People[id] = (name, email);
            if (id != _nokey)
            {
                _archive.Certificates[id] = ["pem"];
                _archive.MyMessages[id] = Guid.NewGuid();
            }
        }

        _conversations = new Conversations(_db, _archive, _caller);
        _files = new Files(_archive, _conversations);
        _messages = new Messages(_db, _conversations, _files, _notices, NullLogger<Messages>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static WrappedKey Key(Guid user) => new(user, [1, 2, 3]);

    private ModuleUser User(Guid id) => _archive.Users.Single(u => u.Id == id);

    private void As(Guid user) => _caller.UserId = user;

    [Fact]
    public async Task A_one_to_one_chat_is_created_once_with_a_folder_in_each_persons_My_Messages()
    {
        As(_anna);
        var id = await _conversations.OpenDirectAsync(User(_tom), [Key(_anna), Key(_tom)], default);
        var again = await _conversations.OpenDirectAsync(User(_tom), [Key(_anna), Key(_tom)], default);

        Assert.Equal(id, again);
        Assert.Equal(_repository, _archive.Documents[id].ParentId);
        Assert.Contains((id, _anna), _archive.Access);
        Assert.Contains((id, _tom), _archive.Access);
        Assert.Equal("Tom", Assert.Single(_archive.Documents.Values, d => d.ParentId == _archive.MyMessages[_anna]).Name);
        Assert.Equal("Anna", Assert.Single(_archive.Documents.Values, d => d.ParentId == _archive.MyMessages[_tom]).Name);

        As(_tom);
        var view = Assert.Single(await _conversations.MineAsync(default));
        Assert.True(view.IsDirect);
        Assert.Equal(_anna, view.CounterpartUserId);
    }

    [Fact]
    public async Task Nobody_joins_without_a_key_and_every_member_needs_exactly_one()
    {
        As(_anna);
        await Assert.ThrowsAsync<ModuleApiException>(() => _conversations.OpenDirectAsync(User(_nokey), [Key(_anna), Key(_nokey)], default));
        await Assert.ThrowsAsync<ModuleApiException>(() => _conversations.CreateGroupAsync("Team", [Key(_tom)], default));   // no key for Anna
    }

    [Fact]
    public async Task Removing_a_member_starts_a_new_epoch_for_those_who_remain_and_the_last_one_out_purges()
    {
        As(_anna);
        var id = await _conversations.CreateGroupAsync("Team", [Key(_anna), Key(_tom), Key(_eve)], default);

        As(_tom);   // not an administrator
        await Assert.ThrowsAsync<ModuleApiException>(() => _conversations.RemoveMemberAsync(id, _eve, [Key(_anna), Key(_tom)], default));

        As(_anna);
        await _conversations.RemoveMemberAsync(id, _eve, [Key(_anna), Key(_tom)], default);
        var view = await _conversations.MemberViewAsync(id, default);
        Assert.Equal(2, view.Epoch);
        Assert.Equal(new[] { _anna, _tom }.Order(), view.Members.Order());
        Assert.DoesNotContain((id, _eve), _archive.Access);

        As(_eve);   // removed: the conversation no longer exists for her
        Assert.Null(await _conversations.ViewAsync(id, default));

        As(_tom);
        await _conversations.RemoveMemberAsync(id, _tom, [], default);   // leaving needs no administrator, and makes no key
        As(_anna);
        await _conversations.RemoveMemberAsync(id, _anna, [], default);
        Assert.Contains(id, _archive.Purged);
        Assert.Empty(await _db.EpochKeys.Where(k => k.ConversationId == id).ToListAsync());
    }

    [Fact]
    public async Task An_administrator_makes_others_administrators_and_a_group_always_keeps_one()
    {
        As(_anna);
        var id = await _conversations.CreateGroupAsync("Team", [Key(_anna), Key(_tom), Key(_eve)], default);
        Assert.Equal([_anna], (await _conversations.MemberViewAsync(id, default)).AdminIds);

        await Assert.ThrowsAsync<ModuleApiException>(() => _conversations.SetAdminAsync(id, _anna, admin: false, default));   // the last one
        await Assert.ThrowsAsync<ModuleApiException>(() => _conversations.RemoveMemberAsync(id, _anna, [], default));

        As(_tom);   // not an administrator
        await Assert.ThrowsAsync<ModuleApiException>(() => _conversations.SetAdminAsync(id, _eve, admin: true, default));

        As(_anna);
        await _conversations.SetAdminAsync(id, _tom, admin: true, default);
        Assert.Equal(new[] { _anna, _tom }.Order(), (await _conversations.MemberViewAsync(id, default)).AdminIds.Order());
        Assert.Equal(["anna@x.io", "tom@x.io"], _archive.Documents[id].FieldLists[MessengerMasks.AdminsField]);   // what the archive shows

        await _conversations.RemoveMemberAsync(id, _anna, [], default);   // now she may leave
        As(_tom);
        var view = await _conversations.MemberViewAsync(id, default);
        Assert.Equal([_tom], view.AdminIds);
        Assert.Equal(["tom@x.io"], view.Admins);
    }

    [Fact]
    public async Task Leaving_closes_the_epoch_until_somebody_who_remains_publishes_the_next()
    {
        As(_anna);
        var id = await _conversations.CreateGroupAsync("Team", [Key(_anna), Key(_tom), Key(_eve)], default);

        As(_eve);
        await Assert.ThrowsAsync<ModuleApiException>(() => _conversations.RemoveMemberAsync(id, _eve, [Key(_anna), Key(_tom)], default));   // a leaver brings no key
        await _conversations.RemoveMemberAsync(id, _eve, [], default);
        Assert.Null(await _conversations.ViewAsync(id, default));

        As(_tom);
        var view = await _conversations.MemberViewAsync(id, default);
        Assert.True(view.NeedsNewEpoch);
        Assert.Equal(new[] { _anna, _tom }.Order(), view.Members.Order());
        var refused = await Assert.ThrowsAsync<ModuleApiException>(() => _messages.SendAsync(id, Guid.NewGuid(), 1, [9], null, null, default));
        Assert.Equal("MESSENGER_REKEY_NEEDED", refused.ErrorCode);

        await Assert.ThrowsAsync<ModuleApiException>(() => _conversations.PublishEpochAsync(id, [Key(_tom)], default));   // a key for everyone
        await _conversations.PublishEpochAsync(id, [Key(_anna), Key(_tom)], default);
        view = await _conversations.MemberViewAsync(id, default);
        Assert.Equal((2, false), (view.Epoch, view.NeedsNewEpoch));
        await _messages.SendAsync(id, Guid.NewGuid(), 2, [9], null, null, default);

        var open = await Assert.ThrowsAsync<ModuleApiException>(() => _conversations.PublishEpochAsync(id, [Key(_anna), Key(_tom)], default));
        Assert.Equal("MESSENGER_EPOCH_OPEN", open.ErrorCode);
    }

    [Fact]
    public async Task A_joiner_gets_a_new_epoch_and_sees_nothing_from_before()
    {
        As(_anna);
        var id = await _conversations.CreateGroupAsync("Team", [Key(_anna), Key(_tom)], default);
        await _conversations.RemoveMemberAsync(id, _tom, [Key(_anna)], default);   // now at epoch 2
        await _messages.SendAsync(id, Guid.NewGuid(), 2, [1], null, null, default);
        await Assert.ThrowsAsync<ModuleApiException>(() => _conversations.AddMemberAsync(id, _eve, [Key(_eve)], default));   // a key for everyone
        await _conversations.AddMemberAsync(id, _eve, [Key(_anna), Key(_eve)], default);

        // A new epoch, and Eve holds only its key: nothing sent before she joined opens for her, nor is it listed.
        Assert.Equal([3], await _db.EpochKeys.Where(k => k.ConversationId == id && k.UserId == _eve).Select(k => k.Epoch).ToListAsync());
        As(_eve);
        Assert.Empty((await _messages.PageAsync(id, null, 50, default)).Page);
    }

    [Fact]
    public async Task Only_the_sender_edits_and_only_under_the_tag_they_read()
    {
        As(_anna);
        var id = await _conversations.CreateGroupAsync("Team", [Key(_anna), Key(_tom)], default);
        await Assert.ThrowsAsync<ModuleApiException>(() => _messages.SendAsync(id, Guid.NewGuid(), 2, [9], null, null, default));   // stale epoch
        var sent = await _messages.SendAsync(id, Guid.NewGuid(), 1, [9], null, null, default);
        var tag = sent.ConcurrencyToken;

        As(_tom);
        await Assert.ThrowsAsync<ModuleApiException>(() => _messages.EditAsync(id, sent.Id, tag, 1, [8], default));   // not his

        As(_anna);
        await Assert.ThrowsAsync<ModuleApiException>(() => _messages.EditAsync(id, sent.Id, null, 1, [8], default));   // 428
        var edited = await _messages.EditAsync(id, sent.Id, tag, 1, [8], default);
        Assert.NotNull(edited.EditedAt);

        _db.ChangeTracker.Clear();
        var stale = await Assert.ThrowsAsync<ModuleApiException>(() => _messages.EditAsync(id, sent.Id, tag, 1, [7], default));
        Assert.Equal(412, stale.StatusCode);
    }

    [Fact]
    public async Task Delete_for_me_hides_it_from_me_only_and_receipts_only_move_forward()
    {
        As(_anna);
        var id = await _conversations.CreateGroupAsync("Team", [Key(_anna), Key(_tom)], default);
        var first = await _messages.SendAsync(id, Guid.NewGuid(), 1, [1], null, null, default);
        await _messages.SendAsync(id, Guid.NewGuid(), 1, [2], null, null, default);

        As(_tom);
        Assert.Equal(2, (await _conversations.MemberViewAsync(id, default)).Unread);
        await _messages.HideForMeAsync(id, first.Id, default);
        Assert.Single((await _messages.PageAsync(id, null, 50, default)).Page);
        await _messages.MarkReadAsync(id, DateTimeOffset.UtcNow.AddMinutes(1), default);
        await _messages.MarkReadAsync(id, DateTimeOffset.UtcNow.AddYears(-1), default);   // an older device changes nothing
        Assert.Equal(0, (await _conversations.MemberViewAsync(id, default)).Unread);

        As(_anna);
        Assert.Equal(2, (await _messages.PageAsync(id, null, 50, default)).Page.Count);   // still there for Anna
    }

    [Fact]
    public async Task Sending_signals_everyone_but_the_sender_with_nothing_of_the_message_and_reading_clears_it()
    {
        As(_anna);
        var id = await _conversations.CreateGroupAsync("Team", [Key(_anna), Key(_tom), Key(_eve)], default);
        await _messages.SendAsync(id, Guid.NewGuid(), 1, [9], null, null, default);

        Assert.Equal(new[] { _tom, _eve }.Order(), _notices.Signals.Select(s => s.RecipientUserId).Order());
        Assert.All(_notices.Signals, s => Assert.Equal((id, "New messages", "Messenger"), (s.DocumentId, s.Title, s.Body)));

        As(_tom);
        await _messages.MarkReadAsync(id, DateTimeOffset.UtcNow, default);
        Assert.Equal([(_tom, id)], _notices.Cleared);
    }

    [Fact]
    public async Task A_file_lives_once_with_a_reference_for_each_person_and_goes_as_each_decides()
    {
        As(_anna);
        var chat = await _conversations.OpenDirectAsync(User(_tom), [Key(_anna), Key(_tom)], default);
        var upload = await _files.BeginAsync(chat, default);
        var file = await _files.CompleteAsync(chat, upload.UploadId, default);

        Assert.Equal(chat, _archive.Documents[file].ParentId);
        Assert.Equal(MessengerMasks.MessageObject, _archive.Documents[file].MaskId);
        var annasFolder = (await _conversations.ChatFolderAsync(_anna, chat, default))!.Value;
        var tomsFolder = (await _conversations.ChatFolderAsync(_tom, chat, default))!.Value;
        Assert.Contains((file, annasFolder), _archive.References);
        Assert.Contains((file, tomsFolder), _archive.References);
        Assert.NotNull(await _files.DownloadAsync(chat, file, default));

        var message = await _messages.SendAsync(chat, Guid.NewGuid(), 1, [1], null, file, default);

        As(_tom);   // delete for me: only Tom's reference goes
        await _messages.HideForMeAsync(chat, message.Id, default);
        Assert.DoesNotContain((file, tomsFolder), _archive.References);
        Assert.Contains((file, annasFolder), _archive.References);

        As(_anna);  // delete for everyone: the file goes, references with it
        await _messages.DeleteForEveryoneAsync(chat, message.Id, message.ConcurrencyToken, default);
        Assert.Contains(file, _archive.Purged);
        Assert.Empty(_archive.References);
    }

    [Fact]
    public async Task A_message_carries_only_a_file_of_its_own_conversation()
    {
        As(_anna);
        var team = await _conversations.CreateGroupAsync("Team", [Key(_anna), Key(_tom)], default);
        var other = await _conversations.CreateGroupAsync("Other", [Key(_anna), Key(_eve)], default);
        var upload = await _files.BeginAsync(other, default);
        var foreign = await _files.CompleteAsync(other, upload.UploadId, default);

        await Assert.ThrowsAsync<ModuleApiException>(() => _messages.SendAsync(team, Guid.NewGuid(), 1, [1], null, foreign, default));
    }
}
