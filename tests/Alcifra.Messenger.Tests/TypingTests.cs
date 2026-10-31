using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Alcifra.Messenger.ReadModel;
using SimplArchive.ModuleAbi;

namespace Alcifra.Messenger.Tests;

// "Is typing" (#26) and the preference behind it: the other members are told, never the sender; the indicator is
// reciprocal, so a sender who switched it off sends nothing and a member who did is not told; a start goes out once per
// interval, a stop always; whatever the hub refuses is dropped. The preference is replaced whole under the tag read.
public sealed class TypingTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly MessengerReadModelContext _db;
    private readonly FakeArchive _archive = new();
    private readonly FakeCaller _caller = new(Guid.NewGuid());
    private readonly FakeLiveEvents _live = new();
    private readonly FakeClock _clock = new();
    private readonly Conversations _conversations;
    private readonly Preferences _preferences;
    private readonly Typing _typing;
    private readonly Guid _anna = Guid.NewGuid(), _tom = Guid.NewGuid(), _eve = Guid.NewGuid();

    public TypingTests()
    {
        _connection.Open();
        _db = new MessengerReadModelContext(new DbContextOptionsBuilder<MessengerReadModelContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _archive.Settings[MessengerModule.RepositorySetting] = Guid.NewGuid().ToString();
        foreach (var (id, name) in new[] { (_anna, "Anna"), (_tom, "Tom"), (_eve, "Eve") })
        {
            var email = $"{name.ToLowerInvariant()}@x.io";
            _archive.Users.Add(new ModuleUser(id, name, email));
            _caller.People[id] = (name, email);
            _archive.Certificates[id] = ["pem"];
            _archive.MyMessages[id] = Guid.NewGuid();
        }

        _conversations = new Conversations(_db, _archive, _caller);
        _preferences = new Preferences(_db, _caller);
        _typing = new Typing(_db, _conversations, _preferences, new TypingThrottle(_clock), _live, _caller, NullLogger<Typing>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private void As(Guid user) => _caller.UserId = user;

    private async Task<Guid> GroupAsync()
    {
        As(_anna);
        return await _conversations.CreateGroupAsync("Team", [new(_anna, [1]), new(_tom, [1]), new(_eve, [1])], default);
    }

    private async Task SwitchOffAsync(Guid user)
    {
        As(user);
        await _preferences.ReplaceAsync(false, Guid.Empty, default);
    }

    [Fact]
    public async Task The_other_members_are_told_who_types_where_and_the_sender_is_not()
    {
        var id = await GroupAsync();

        As(_tom);
        Assert.True(await _typing.SetAsync(id, typing: true, default));

        var sent = Assert.Single(_live.Sent);
        Assert.Equal("typing", sent.Name);
        Assert.Equal(new[] { _anna, _eve }.Order(), sent.RecipientUserIds.Order());
        var payload = JsonDocument.Parse(sent.Payload).RootElement;
        Assert.Equal(id, payload.GetProperty("conversation").GetGuid());
        Assert.Equal(_tom, payload.GetProperty("user").GetGuid());
        Assert.True(payload.GetProperty("typing").GetBoolean());
    }

    [Fact]
    public async Task A_sender_who_switched_it_off_sends_nothing_at_all()
    {
        var id = await GroupAsync();
        await SwitchOffAsync(_tom);

        As(_tom);
        Assert.False(await _typing.SetAsync(id, typing: true, default));
        Assert.False(await _typing.SetAsync(id, typing: false, default));
        Assert.Empty(_live.Sent);
    }

    [Fact]
    public async Task A_member_who_switched_it_off_is_not_told()
    {
        var id = await GroupAsync();
        await SwitchOffAsync(_eve);

        As(_tom);
        await _typing.SetAsync(id, typing: true, default);
        Assert.Equal([_anna], Assert.Single(_live.Sent).RecipientUserIds);
    }

    [Fact]
    public async Task A_start_goes_out_once_per_interval_and_a_stop_always_and_lets_the_next_start_through()
    {
        var id = await GroupAsync();
        As(_tom);

        Assert.True(await _typing.SetAsync(id, true, default));
        _clock.Now += TimeSpan.FromSeconds(1);
        Assert.False(await _typing.SetAsync(id, true, default));   // held back: the members' apps still show it
        _clock.Now += TimeSpan.FromSeconds(2);
        Assert.True(await _typing.SetAsync(id, true, default));    // the interval is over: a refresh

        Assert.True(await _typing.SetAsync(id, false, default));   // sent the message
        Assert.True(await _typing.SetAsync(id, true, default));    // and goes on typing: shown again at once
        Assert.Equal(4, _live.Sent.Count);
    }

    [Fact]
    public void The_throttle_is_per_sender_and_conversation()
    {
        var throttle = new TypingThrottle(_clock);
        var (tenant, a, b, one, two) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.True(throttle.LetsStartThrough(tenant, a, one));
        Assert.False(throttle.LetsStartThrough(tenant, a, one));
        Assert.True(throttle.LetsStartThrough(tenant, b, one));   // another sender
        Assert.True(throttle.LetsStartThrough(tenant, a, two));   // another conversation
        _clock.Now += TypingThrottle.Interval;
        Assert.True(throttle.LetsStartThrough(tenant, a, one));
    }

    [Fact]
    public async Task What_the_hub_refuses_is_dropped()
    {
        var id = await GroupAsync();
        _live.Refuse = new ModuleLiveEventRateLimitedException();

        As(_tom);
        Assert.False(await _typing.SetAsync(id, true, default));   // no exception: a typing state is not worth a retry
    }

    [Fact]
    public async Task Only_a_member_says_they_type_and_nobody_types_in_history()
    {
        var id = await GroupAsync();
        As(_anna);
        await _conversations.RemoveMemberAsync(id, _eve, [new(_anna, [1]), new(_tom, [1])], default);

        As(_eve);   // removed: not a member any more
        Assert.Equal("MESSENGER_NOT_A_MEMBER", (await Assert.ThrowsAsync<ModuleApiException>(() => _typing.SetAsync(id, true, default))).ErrorCode);

        As(_tom);
        await _typing.SetAsync(id, true, default);
        Assert.Equal([_anna], Assert.Single(_live.Sent).RecipientUserIds);   // the removed member is not told either
    }

    [Fact]
    public async Task The_preference_is_on_until_switched_off_and_replaced_only_under_the_tag_read()
    {
        As(_tom);
        var defaults = await _preferences.MineAsync(default);
        Assert.True(defaults.TypingIndicator);
        Assert.Equal(Guid.Empty, defaults.ConcurrencyToken);   // no row yet: the defaults' tag

        await Assert.ThrowsAsync<ModuleApiException>(() => _preferences.ReplaceAsync(false, null, default));   // 428
        var offTag = (await _preferences.ReplaceAsync(false, Guid.Empty, default)).ConcurrencyToken;
        Assert.False((await _preferences.MineAsync(default)).TypingIndicator);

        // Another device still holding the defaults' tag (or any stale one) is refused, not silently applied.
        var stale = await Assert.ThrowsAsync<ModuleApiException>(() => _preferences.ReplaceAsync(true, Guid.Empty, default));
        Assert.Equal(412, stale.StatusCode);
        _db.ChangeTracker.Clear();
        var on = await _preferences.ReplaceAsync(true, offTag, default);
        Assert.True(on.TypingIndicator);
        Assert.NotEqual(offTag, on.ConcurrencyToken);

        As(_anna);   // one user's preference is theirs alone
        Assert.True((await _preferences.MineAsync(default)).TypingIndicator);
    }
}
