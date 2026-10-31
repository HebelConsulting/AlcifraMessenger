using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Alcifra.Messenger.Client;
using Alcifra.Messenger.Client.Api;
using Alcifra.Messenger.Client.Backup;

namespace Alcifra.Messenger.Tests;

// Backup and restore (core #1813) with REAL certificates across two installations: the same people have NEW ids on the
// second, so a restored message opens only if the restore kept the ids its ciphertext is bound to.
public sealed class BackupTests : IAsyncDisposable
{
    private readonly ServerRig _old = new(), _new = new();
    private readonly Dictionary<string, X509Certificate2> _keys = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var key in _keys.Values)
        {
            key.Dispose();
        }

        await _old.DisposeAsync();
        await _new.DisposeAsync();
    }

    // The same person on either installation: one certificate (one hardware key), a new user id on each.
    private Guid Person(ServerRig rig, string name)
    {
        if (!_keys.TryGetValue(name, out var key))
        {
            using var rsa = RSA.Create(2048);
            using var made = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            _keys[name] = key = X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pfx), null);
        }

        return rig.AddPerson(name, key.ExportCertificatePem());
    }

    private ChatSession Session(ServerRig rig, Guid id, string name) =>
        new(rig.Api, new PrivateKeyOpener(_keys[name]), id, $"{name.ToLowerInvariant()}@x.io");

    private async Task<(MemoryStream Annas, MemoryStream Toms, byte[] Photo)> TalkAndBackUpAsync()
    {
        var (anna, tom) = (Person(_old, "Anna"), Person(_old, "Tom"));
        await _old.StartAsync();
        _old.Caller.UserId = anna;
        await _old.Api.DiscoverAsync();
        var annas = Session(_old, anna, "Anna");
        var group = await annas.CreateGroupAsync(await _old.Api.ConversationsAsync(), "Crew", [Assert.Single((await _old.Api.PeopleAsync("tom")).Items)]);
        var (page, _) = await annas.MessagesAsync(group);
        await annas.SendAsync(group, page, "Hello Tom");

        _old.Caller.UserId = tom;
        var forTom = await _old.Api.RefreshAsync(group);
        var photo = RandomNumberGenerator.GetBytes(2048);
        var (tomsPage, _) = await Session(_old, tom, "Tom").MessagesAsync(forTom);
        await Session(_old, tom, "Tom").SendFileAsync(forTom, tomsPage, photo, "harbour.jpg", "image/jpeg", "Look");

        var toms = new MemoryStream();
        Assert.Equal(new BackupSummary(1, 2, 1), await MessengerBackup.WriteAsync(_old.Api, toms, "https://old.example"));
        _old.Caller.UserId = anna;
        var annasBackup = new MemoryStream();
        await MessengerBackup.WriteAsync(_old.Api, annasBackup, "https://old.example");
        return (Rewound(annasBackup), Rewound(toms), photo);
    }

    [Fact]
    public async Task A_restored_conversation_opens_on_another_installation_with_the_same_key_and_is_read_only()
    {
        var (backup, _, photo) = await TalkAndBackUpAsync();
        var anna = Person(_new, "Anna");   // a new id here; the same key
        await _new.StartAsync();
        _new.Caller.UserId = anna;
        await _new.Api.DiscoverAsync();

        var report = await MessengerBackup.RestoreAsync(_new.Api, backup);

        Assert.Equal((1, 2, 0, 1), (report.Conversations, report.Imported, report.Skipped, report.Files));
        var restored = Assert.Single((await _new.Api.ConversationsAsync()).Items);
        Assert.True(restored.Restored && restored.IsReadOnly);
        Assert.Equal("Crew (restored)", restored.Name);
        Assert.Equal("https://old.example", restored.RestoredFrom);
        Assert.NotEqual(restored.Id, restored.SealedUnder);    // a new folder here, bound to the old id
        Assert.NotEqual(anna, restored.MyUserId);               // Anna's own old messages carry her id there

        var session = Session(_new, anna, "Anna");
        var (first, messages) = await session.MessagesAsync(restored);
        var opened = new List<OpenedMessage>();
        foreach (var message in messages)
        {
            opened.Add(await session.OpenAsync(restored, message));
        }

        Assert.Equal(["Hello Tom", "Look"], opened.Select(o => o.Text));
        Assert.All(messages, m => Assert.True(m.Restored));
        Assert.Equal(photo, await session.OpenFileAsync(opened[1].Message));   // the envelope, sealed to Anna's key at send time
        Assert.Null(first.LinkOf("send"));                                    // history: nothing is sent into it
        Assert.All(messages, m => Assert.Null(m.LinkOf("edit")));
    }

    [Fact]
    public async Task Restoring_twice_adds_nothing_and_a_second_member_joins_the_same_conversation()
    {
        var (annas, toms, _) = await TalkAndBackUpAsync();
        var (anna, tom) = (Person(_new, "Anna"), Person(_new, "Tom"));
        await _new.StartAsync();
        _new.Caller.UserId = anna;
        await _new.Api.DiscoverAsync();
        await MessengerBackup.RestoreAsync(_new.Api, annas);

        var again = await MessengerBackup.RestoreAsync(_new.Api, Rewound(annas));
        Assert.Equal((1, 0, 2, 0), (again.Conversations, again.Imported, again.Skipped, again.Files));

        _new.Caller.UserId = tom;
        var joined = await MessengerBackup.RestoreAsync(_new.Api, toms);
        Assert.Equal((0, 2), (joined.Imported, joined.Skipped));
        var forTom = Assert.Single((await _new.Api.ConversationsAsync()).Items);
        _new.Caller.UserId = anna;
        Assert.Equal(Assert.Single((await _new.Api.ConversationsAsync()).Items).Id, forTom.Id);   // one conversation, not two
    }

    [Fact]
    public async Task Somebody_else_cannot_restore_my_backup_and_a_changed_backup_is_refused()
    {
        var (annas, _, _) = await TalkAndBackUpAsync();
        var ina = Person(_new, "Ina");
        await _new.StartAsync();
        _new.Caller.UserId = ina;
        await _new.Api.DiscoverAsync();

        var refused = await Assert.ThrowsAsync<MessengerApiException>(() => MessengerBackup.RestoreAsync(_new.Api, annas));
        Assert.Equal("MESSENGER_NOT_YOUR_BACKUP", refused.Code);

        var changed = new MemoryStream();
        Rewound(annas).CopyTo(changed);
        using (var zip = new ZipArchive(Rewound(changed), ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.Entries.First(e => e.FullName.EndsWith("messages.jsonl", StringComparison.Ordinal));
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry(entry.FullName).Open());
            writer.Write("{}");
        }

        await Assert.ThrowsAsync<BackupDamagedException>(() => MessengerBackup.RestoreAsync(_new.Api, Rewound(changed)));
    }

    [Fact]
    public async Task A_restored_group_continues_as_a_live_group_with_its_history_above_it()
    {
        var (annas, _, _) = await TalkAndBackUpAsync();
        var (anna, tom) = (Person(_new, "Anna"), Person(_new, "Tom"));
        await _new.StartAsync();
        _new.Caller.UserId = anna;
        await _new.Api.DiscoverAsync();
        await MessengerBackup.RestoreAsync(_new.Api, annas);
        var restored = Assert.Single((await _new.Api.ConversationsAsync()).Items);

        var session = Session(_new, anna, "Anna");
        var live = await session.ContinueAsync(restored);

        Assert.Equal("Crew", live.Name);
        Assert.False(live.IsReadOnly || live.Restored);
        Assert.True(live.CanManageMembers);                                          // the member who continued it
        Assert.Equal(new[] { anna, tom }.Order(), live.Members.Select(m => m.UserId).Order());
        Assert.Equal(live.Id, Assert.Single((await _new.Api.ConversationsAsync()).Items).Id);   // the history is no entry of its own
        var earlier = Assert.Single((await _new.Api.EarlierAsync(live)).Items);
        Assert.Equal(restored.Id, earlier.Id);
        Assert.Null(earlier.LinkOf("continuation"));                                 // continued once

        var (page, _) = await session.MessagesAsync(live);
        await session.SendAsync(live, page, "Back again");
        _new.Caller.UserId = tom;                                                    // Tom never restored: he reads what follows
        var forTom = await _new.Api.RefreshAsync(live);
        var (_, messages) = await Session(_new, tom, "Tom").MessagesAsync(forTom);
        Assert.Equal("Back again", (await Session(_new, tom, "Tom").OpenAsync(forTom, Assert.Single(messages))).Text);
        Assert.Empty((await _new.Api.EarlierAsync(forTom)).Items);                   // and no history he holds no key for
    }

    [Fact]
    public async Task A_restored_chat_continues_as_the_chat_with_that_person_already_started_here()
    {
        var (anna, tom) = (Person(_old, "Anna"), Person(_old, "Tom"));
        await _old.StartAsync();
        _old.Caller.UserId = anna;
        await _old.Api.DiscoverAsync();
        var old = Session(_old, anna, "Anna");
        var chat = await old.StartDirectAsync(await _old.Api.ConversationsAsync(), Assert.Single((await _old.Api.PeopleAsync("tom")).Items));
        var (oldPage, _) = await old.MessagesAsync(chat);
        await old.SendAsync(chat, oldPage, "Hi from before");
        var backup = new MemoryStream();
        await MessengerBackup.WriteAsync(_old.Api, backup, "https://old.example");

        var (annaHere, _) = (Person(_new, "Anna"), Person(_new, "Tom"));
        await _new.StartAsync();
        _new.Caller.UserId = annaHere;
        await _new.Api.DiscoverAsync();
        var session = Session(_new, annaHere, "Anna");
        var existing = await session.StartDirectAsync(await _new.Api.ConversationsAsync(), Assert.Single((await _new.Api.PeopleAsync("tom")).Items));
        await MessengerBackup.RestoreAsync(_new.Api, Rewound(backup));
        var restored = (await _new.Api.ConversationsAsync()).Items.Single(c => c.Restored);
        Assert.True(restored.IsDirect);
        Assert.Equal("Tom", restored.CounterpartName);
        Assert.Equal(existing.Id, (await _new.Api.ContinuationAsync(restored)).ExistingConversationId);

        var live = await session.ContinueAsync(restored);

        Assert.Equal(existing.Id, live.Id);                                          // joined, not started again
        var earlier = Assert.Single((await _new.Api.EarlierAsync(live)).Items);
        var (_, history) = await session.MessagesAsync(earlier);
        Assert.Equal("Hi from before", (await session.OpenAsync(earlier, Assert.Single(history))).Text);
    }

    [Fact]
    public async Task Somebody_not_here_keeps_it_history_and_is_named()
    {
        var (annas, _, _) = await TalkAndBackUpAsync();
        var anna = Person(_new, "Anna");                                             // Tom has no account here
        await _new.StartAsync();
        _new.Caller.UserId = anna;
        await _new.Api.DiscoverAsync();
        await MessengerBackup.RestoreAsync(_new.Api, annas);
        var restored = Assert.Single((await _new.Api.ConversationsAsync()).Items);

        var blocked = await Assert.ThrowsAsync<ContinuationBlockedException>(() => Session(_new, anna, "Anna").ContinueAsync(restored));

        Assert.Equal(["Tom"], blocked.Missing);
        Assert.True(Assert.Single((await _new.Api.ConversationsAsync()).Items).IsReadOnly);
    }

    [Fact]
    public async Task A_conversation_one_left_is_backed_up_for_the_stretch_one_was_in()
    {
        var (anna, tom) = (Person(_old, "Anna"), Person(_old, "Tom"));
        await _old.StartAsync();
        _old.Caller.UserId = anna;
        await _old.Api.DiscoverAsync();
        var annas = Session(_old, anna, "Anna");
        var group = await annas.CreateGroupAsync(await _old.Api.ConversationsAsync(), "Crew", [Assert.Single((await _old.Api.PeopleAsync("tom")).Items)]);
        var (page, _) = await annas.MessagesAsync(group);
        await annas.SendAsync(group, page, "Before Tom left");
        _old.Caller.UserId = tom;
        await Session(_old, tom, "Tom").LeaveAsync(await _old.Api.RefreshAsync(group));
        var backup = new MemoryStream();
        Assert.Equal(new BackupSummary(1, 1, 0), await MessengerBackup.WriteAsync(_old.Api, backup, "https://old.example"));

        var tomHere = Person(_new, "Tom");
        await _new.StartAsync();
        _new.Caller.UserId = tomHere;
        await _new.Api.DiscoverAsync();
        await MessengerBackup.RestoreAsync(_new.Api, Rewound(backup));
        var restored = Assert.Single((await _new.Api.ConversationsAsync()).Items);
        var session = Session(_new, tomHere, "Tom");
        var (_, messages) = await session.MessagesAsync(restored);
        Assert.Equal("Before Tom left", (await session.OpenAsync(restored, Assert.Single(messages))).Text);
    }

    private static MemoryStream Rewound(MemoryStream stream)
    {
        stream.Position = 0;
        return stream;
    }
}
