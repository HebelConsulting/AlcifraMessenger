using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using SimplArchive.Messenger.Client;
using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Tests;

// A session per person, each with a REAL certificate and a key opener holding its private key, against the real
// controllers: every key is sealed to certificates and opened again, so a message read here was truly end to end.
public sealed class ChatSessionTests : IAsyncDisposable
{
    private readonly ServerRig _rig = new();
    private readonly Dictionary<Guid, (X509Certificate2 Certificate, string Email)> _people = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var (certificate, _) in _people.Values)
        {
            certificate.Dispose();
        }

        await _rig.DisposeAsync();
    }

    private Guid Person(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var id = _rig.AddPerson(name, certificate.ExportCertificatePem());
        _people[id] = (X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null), $"{name.ToLowerInvariant()}@x.io");
        certificate.Dispose();
        return id;
    }

    // Sessions share the one API client; who is asking is switched on the rig before each act.
    private ChatSession SessionOf(Guid person) => new(_rig.Api, new PrivateKeyOpener(_people[person].Certificate), person, _people[person].Email);

    private async Task<Conversation> AsAsync(Guid person, Conversation conversation)
    {
        _rig.Caller.UserId = person;
        return await _rig.Api.RefreshAsync(conversation);
    }

    private async Task<IReadOnlyList<string?>> ReadAllAsync(ChatSession session, Conversation conversation)
    {
        var (_, messages) = await session.MessagesAsync(conversation);
        var opened = new List<string?>();
        foreach (var message in messages)
        {
            opened.Add((await session.OpenAsync(conversation, message)).Text);
        }

        return opened;
    }

    [Fact]
    public async Task A_one_to_one_chat_is_sealed_on_one_phone_and_opened_on_the_other()
    {
        var (anna, tom) = (Person("Anna"), Person("Tom"));
        await _rig.StartAsync();
        _rig.Caller.UserId = anna;
        await _rig.Api.DiscoverAsync();
        var annas = SessionOf(anna);

        var tomAsPerson = Assert.Single((await _rig.Api.PeopleAsync("tom")).Items);
        var chat = await annas.StartDirectAsync(await _rig.Api.ConversationsAsync(), tomAsPerson);
        var (page, _) = await annas.MessagesAsync(chat);
        await annas.SendAsync(chat, page, "Hello Tom");

        var forTom = await AsAsync(tom, chat);
        Assert.Equal(["Hello Tom"], await ReadAllAsync(SessionOf(tom), forTom));
    }

    [Fact]
    public async Task A_photo_is_sealed_to_every_member_and_opened_with_the_receivers_own_key()
    {
        var (anna, tom) = (Person("Anna"), Person("Tom"));
        await _rig.StartAsync();
        _rig.Caller.UserId = anna;
        await _rig.Api.DiscoverAsync();
        var annas = SessionOf(anna);
        var chat = await annas.StartDirectAsync(await _rig.Api.ConversationsAsync(), Assert.Single((await _rig.Api.PeopleAsync("tom")).Items));
        var (page, _) = await annas.MessagesAsync(chat);
        var photo = System.Security.Cryptography.RandomNumberGenerator.GetBytes(4096);

        await annas.SendFileAsync(chat, page, photo, "harbour.jpg", "image/jpeg", "Look at this");

        var forTom = await AsAsync(tom, chat);
        var toms = SessionOf(tom);
        var (_, messages) = await toms.MessagesAsync(forTom);
        var opened = await toms.OpenAsync(forTom, Assert.Single(messages));
        Assert.Equal("Look at this", opened.Text);
        Assert.Equal(new SentFile("harbour.jpg", "image/jpeg"), opened.File);
        Assert.Equal(photo, await toms.OpenFileAsync(opened.Message));   // sealed to Tom's certificate, opened with his key
    }

    [Fact]
    public async Task After_somebody_leaves_the_next_message_rekeys_by_itself_and_the_leaver_reads_nothing_new()
    {
        var (anna, tom, eve) = (Person("Anna"), Person("Tom"), Person("Eve"));
        await _rig.StartAsync();
        _rig.Caller.UserId = anna;
        await _rig.Api.DiscoverAsync();
        var annas = SessionOf(anna);
        var people = (await _rig.Api.PeopleAsync("@x.io")).Items;
        var group = await annas.CreateGroupAsync(await _rig.Api.ConversationsAsync(), "Team", people);
        var (page, _) = await annas.MessagesAsync(group);
        await annas.SendAsync(group, page, "Before");

        var forEve = await AsAsync(eve, group);
        await SessionOf(eve).LeaveAsync(forEve);

        var forTom = await AsAsync(tom, group);
        Assert.True(forTom.NeedsNewEpoch);
        var toms = SessionOf(tom);
        await toms.SendAsync(forTom, page, "After");   // publishes epoch 2 first, by itself

        var forAnna = await AsAsync(anna, group);
        Assert.Equal((2, false), (forAnna.Epoch, forAnna.NeedsNewEpoch));
        Assert.Equal(["Before", "After"], await ReadAllAsync(annas, forAnna));   // the old message opens with epoch 1's key
        Assert.DoesNotContain(forAnna.Members, m => m.UserId == eve);

        // Eve left: the stretch she was a member for stays hers to read, as history, and nothing after it.
        _rig.Caller.UserId = eve;
        var hers = Assert.Single((await _rig.Api.ConversationsAsync()).Items);
        Assert.True(hers.Left && hers.IsReadOnly);
        Assert.Equal(["Before"], await ReadAllAsync(SessionOf(eve), hers));
        var (herPage, _) = await SessionOf(eve).MessagesAsync(hers);
        Assert.Null(herPage.LinkOf("send"));                                     // history: nothing is sent into it
        Assert.Null(hers.LinkOf("upload-file"));
        Assert.All(hers.Members, m => Assert.Null(m.LinkOf("remove")));
    }

    [Fact]
    public async Task A_removed_member_keeps_reading_what_was_sealed_while_they_were_in()
    {
        var (anna, tom) = (Person("Anna"), Person("Tom"));
        var ina = Person("Ina");
        await _rig.StartAsync();
        _rig.Caller.UserId = anna;
        await _rig.Api.DiscoverAsync();
        var annas = SessionOf(anna);
        var group = await annas.CreateGroupAsync(await _rig.Api.ConversationsAsync(), "Crew", (await _rig.Api.PeopleAsync("@x.io")).Items);
        var (page, _) = await annas.MessagesAsync(group);
        await annas.SendAsync(group, page, "While Tom is in");
        await annas.RemoveAsync(group, group.Members.Single(m => m.UserId == tom));
        var now = await AsAsync(anna, group);
        var (later, _) = await annas.MessagesAsync(now);
        await annas.SendAsync(now, later, "After Tom");

        var toms = Assert.Single((await AsListOfAsync(tom)).Items);
        Assert.True(toms.Left);
        Assert.Equal(["While Tom is in"], await ReadAllAsync(SessionOf(tom), toms));
        Assert.Equal(["While Tom is in", "After Tom"], await ReadAllAsync(SessionOf(ina), await AsAsync(ina, group)));
    }

    private async Task<ConversationList> AsListOfAsync(Guid person)
    {
        _rig.Caller.UserId = person;
        return await _rig.Api.ConversationsAsync();
    }

    [Fact]
    public async Task Adding_and_removing_each_start_a_new_key_so_a_joiner_reads_only_from_joining_on()
    {
        var (anna, tom, eve) = (Person("Anna"), Person("Tom"), Person("Eve"));
        await _rig.StartAsync();
        _rig.Caller.UserId = anna;
        await _rig.Api.DiscoverAsync();
        var annas = SessionOf(anna);
        var everyone = (await _rig.Api.PeopleAsync("@x.io")).Items;
        var group = await annas.CreateGroupAsync(await _rig.Api.ConversationsAsync(), "Team", [.. everyone.Where(p => p.Id == tom)]);
        var (page, _) = await annas.MessagesAsync(group);
        await annas.SendAsync(group, page, "Only for Tom");

        await annas.AddAsync(group, everyone.Single(p => p.Id == eve));
        group = await _rig.Api.RefreshAsync(group);
        await annas.SendAsync(group, page, "Welcome Eve");
        await annas.RemoveAsync(group, group.Members.Single(m => m.UserId == tom));
        group = await _rig.Api.RefreshAsync(group);
        await annas.SendAsync(group, page, "Tom is gone");

        var forEve = await AsAsync(eve, group);
        Assert.Equal(["Welcome Eve", "Tom is gone"], await ReadAllAsync(SessionOf(eve), forEve));   // nothing from before she joined

        var forAnna = await AsAsync(anna, group);
        Assert.Equal(3, forAnna.Epoch);   // one for the add, one for the removal
        Assert.Equal(["Only for Tom", "Welcome Eve", "Tom is gone"], await ReadAllAsync(annas, forAnna));
    }
}
