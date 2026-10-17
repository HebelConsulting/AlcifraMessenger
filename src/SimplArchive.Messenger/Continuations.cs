using Microsoft.EntityFrameworkCore;
using SimplArchive.Messenger.ReadModel;
using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger;

/// <summary>
/// Continuing a restored conversation here (core #1813): its history cannot move into another conversation's epochs —
/// every message is bound to the ids it was sealed under — so it continues in a LIVE conversation with that history
/// shown above it. A one-to-one chat continues as the chat with that person (the one already started here, if any); a
/// group as a new group of the same people, administered by the member who continued it. Every member must be here,
/// matched by e-mail to a user with an enrolled key; otherwise it stays history.
/// </summary>
public sealed class Continuations(
    MessengerReadModelContext db, IModuleArchiveFacade archive, Conversations conversations)
{
    /// <summary>An old member, and who they are here: null when nobody with an enrolled key has their e-mail.</summary>
    public sealed record Match(OriginMember Origin, ModuleUser? Here);

    /// <summary>
    /// Who the restored conversation's members are here, and for a one-to-one chat the chat with that person already
    /// started here, which continuing would join rather than start.
    /// </summary>
    public sealed record Preview(Conversations.View Restored, IReadOnlyList<Match> Members, Guid? ExistingChat, string Name)
    {
        public IReadOnlyList<string> Missing => [.. Members.Where(m => m.Here is null).Select(m => m.Origin.DisplayName)];
    }

    public async Task<Preview> PreviewAsync(Guid restoredId, CancellationToken cancellationToken)
    {
        var view = await conversations.MemberViewAsync(restoredId, cancellationToken);
        if (view.Restored is not { IsReadOnly: true } restored)
        {
            throw MessengerErrors.Invalid("Only restored history can be continued.");
        }

        if (restored.ContinuedInConversationId is not null)
        {
            throw MessengerErrors.AlreadyContinued();
        }

        var origin = await db.OriginMembers.AsNoTracking().Where(o => o.ConversationId == restoredId).ToListAsync(cancellationToken);
        var matches = new List<Match>();
        foreach (var member in origin)
        {
            // The search offers only people with an enrolled key: a member without one could not read what follows.
            var here = (await archive.SearchUsersAsync(member.Email, 5, cancellationToken))
                .FirstOrDefault(u => u.Email.Equals(member.Email, StringComparison.OrdinalIgnoreCase));
            matches.Add(new Match(member, here));
        }

        Guid? existing = null;
        if (restored.IsDirect && matches.FirstOrDefault(m => m.Here is { } u && u.Id != conversations.Me)?.Here is { } other)
        {
            var (a, b) = conversations.Me.CompareTo(other.Id) < 0 ? (conversations.Me, other.Id) : (other.Id, conversations.Me);
            existing = (await db.DirectConversations.AsNoTracking().FirstOrDefaultAsync(d => d.UserA == a && d.UserB == b, cancellationToken))?.ConversationId;
        }

        return new Preview(view, matches, existing, LiveName(view.Name));
    }

    /// <summary>
    /// Continues the restored conversation, in one transaction: opens the chat (joining the one already here) or creates
    /// the group with <paramref name="keys"/> — a new key for every member, sealed on the caller's device — and places
    /// the history before it. Returns the live conversation.
    /// </summary>
    public async Task<Guid> ContinueAsync(Guid restoredId, IReadOnlyList<WrappedKey> keys, CancellationToken cancellationToken)
    {
        var preview = await PreviewAsync(restoredId, cancellationToken);
        if (preview.Missing.Count > 0)
        {
            throw MessengerErrors.MembersMissing(preview.Missing);
        }

        var live = Guid.Empty;
        await archive.InTransactionAsync(async () =>
        {
            if (preview.Restored.IsDirect)
            {
                var other = preview.Members.Select(m => m.Here!).First(u => u.Id != conversations.Me);
                live = preview.ExistingChat ?? await conversations.OpenDirectAsync(other, keys, cancellationToken);
            }
            else
            {
                var repository = Guid.Parse((await archive.GetSettingAsync(MessengerModule.RepositorySetting, cancellationToken))!);
                var name = await FolderNames.FreeAsync(archive, repository, n => n == 1 ? preview.Name : $"{preview.Name} ({n})", cancellationToken);
                live = await conversations.CreateGroupAsync(name, keys, cancellationToken);
            }

            var row = await db.RestoredConversations.SingleAsync(r => r.ConversationId == restoredId, cancellationToken);
            row.ContinuedInConversationId = live;
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
        return live;
    }

    /// <summary>The restored histories that sit before <paramref name="liveId"/> and that the caller holds keys for, oldest first.</summary>
    public async Task<IReadOnlyList<Conversations.View>> EarlierAsync(Guid liveId, CancellationToken cancellationToken)
    {
        await conversations.MemberViewAsync(liveId, cancellationToken);
        // Ordered in memory: SQLite (the tests' provider) cannot order by DateTimeOffset, and the list is short.
        var ids = (await db.RestoredConversations.AsNoTracking().Where(r => r.ContinuedInConversationId == liveId)
                .Select(r => new { r.ConversationId, r.RestoredAt }).ToListAsync(cancellationToken))
            .OrderBy(r => r.RestoredAt).Select(r => r.ConversationId).ToList();
        var views = new List<Conversations.View>();
        foreach (var id in ids)
        {
            if (await conversations.ViewAsync(id, cancellationToken) is { } view)
            {
                views.Add(view);
            }
        }

        return views;
    }

    /// <summary>Whether any restored history sits before <paramref name="liveId"/>: what offers the <c>earlier</c> rel.</summary>
    public Task<bool> HasEarlierAsync(Guid liveId, CancellationToken cancellationToken) =>
        db.RestoredConversations.AnyAsync(r => r.ContinuedInConversationId == liveId, cancellationToken);

    // "Crew (restored)" continues as "Crew": the live conversation is the conversation again.
    private static string LiveName(string restoredName)
    {
        var cut = restoredName.LastIndexOf(" (restored", StringComparison.Ordinal);
        return cut > 0 ? restoredName[..cut] : restoredName;
    }
}
