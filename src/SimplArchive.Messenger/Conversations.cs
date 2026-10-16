using Microsoft.EntityFrameworkCore;
using SimplArchive.Messenger.ReadModel;
using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger;

/// <summary>A member's wrapped copy of an epoch's conversation key, as a client sends it.</summary>
public sealed record WrappedKey(Guid UserId, byte[] Key);

/// <summary>
/// The Messenger's rules: who is in a conversation, which key epoch it is at, and what a member may do. The server never
/// sees a key or a message in the clear; it checks membership, epochs and ownership, and keeps the archive's folders and
/// access lists in step.
/// </summary>
/// <remarks>
/// <b>Membership is the latest epoch's key rows.</b> Whoever holds a key for the current epoch is a member; the folder's
/// access list mirrors that, so the archive shows the same people. A new epoch is written whenever somebody leaves, with
/// keys for the members who remain, so a removed member cannot read what follows.
/// </remarks>
public sealed class Conversations(MessengerReadModelContext db, IModuleArchiveFacade archive, IModuleCallerContext caller)
{
    /// <summary>The conversation as one member sees it.</summary>
    public sealed record View(
        Guid Id, bool IsDirect, string Name, Guid? CounterpartUserId, int Epoch, byte[] MyKey,
        IReadOnlyList<Guid> Members, IReadOnlyList<string> Admins, int Unread, bool IAmAdmin, Guid Me)
    {
        /// <summary>The members who are administrators: the Admins field keeps e-mails, so each member is looked up once.</summary>
        public IReadOnlySet<Guid> AdminIds { get; init; } = new HashSet<Guid>();

        /// <summary>Each member's display name, for the thread's senders and a one-to-one chat's title.</summary>
        public IReadOnlyDictionary<Guid, string> Names { get; init; } = new Dictionary<Guid, string>();

        /// <summary>The caller's copy of EVERY epoch's key they hold: messages from before a removal stay readable.</summary>
        public IReadOnlyDictionary<int, byte[]> MyKeys { get; init; } = new Dictionary<int, byte[]>();
    }

    public Guid Me => caller.UserId ?? throw MessengerErrors.NotSignedIn();

    /// <summary>Every conversation the caller is a member of, most recent first.</summary>
    public async Task<IReadOnlyList<View>> MineAsync(CancellationToken cancellationToken)
    {
        var me = Me;
        var ids = await db.EpochKeys.Where(k => k.UserId == me).Select(k => k.ConversationId).Distinct().ToListAsync(cancellationToken);
        var views = new List<View>();
        foreach (var id in ids)
        {
            if (await ViewAsync(id, cancellationToken) is { } view)
            {
                views.Add(view);
            }
        }

        return views;
    }

    /// <summary>One conversation, or null when the caller is not a member at its current epoch.</summary>
    public async Task<View?> ViewAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var me = Me;
        var epoch = await CurrentEpochAsync(conversationId, cancellationToken);
        var keys = await db.EpochKeys.Where(k => k.ConversationId == conversationId && k.Epoch == epoch).ToListAsync(cancellationToken);
        if (keys.FirstOrDefault(k => k.UserId == me) is not { } mine)
        {
            return null;
        }

        var direct = await db.DirectConversations.FirstOrDefaultAsync(d => d.ConversationId == conversationId, cancellationToken);
        var folder = await archive.GetDocumentAsync(conversationId, cancellationToken);
        var admins = folder?.FieldLists.GetValueOrDefault(MessengerMasks.AdminsField) ?? [];
        var myEmail = (await caller.GetIdentityAsync(cancellationToken))?.Email;
        var readUpTo = await db.ReadReceipts.Where(r => r.ConversationId == conversationId && r.UserId == me)
            .Select(r => (DateTimeOffset?)r.ReadUpTo).FirstOrDefaultAsync(cancellationToken);
        var hidden = db.HiddenMessages.Where(h => h.UserId == me).Select(h => h.MessageId);
        var unread = (await db.Messages
                .Where(m => m.ConversationId == conversationId && m.SenderUserId != me && m.DeletedAt == null && !hidden.Contains(m.Id))
                .Select(m => m.SentAt).ToListAsync(cancellationToken))
            .Count(sent => readUpTo is null || sent > readUpTo);   // DateTimeOffset compared in memory: SQLite cannot

        // Each member looked up once: their name for the screens, their e-mail against the Admins field.
        var adminIds = new HashSet<Guid>();
        var names = new Dictionary<Guid, string>();
        foreach (var member in keys.Select(k => k.UserId))
        {
            if (await archive.GetUserAsync(member, cancellationToken) is not { } person)
            {
                continue;   // deactivated since: still a member by key, shown without a name
            }

            names[member] = person.DisplayName;
            if (direct is null && admins.Contains(person.Email, StringComparer.OrdinalIgnoreCase))
            {
                adminIds.Add(member);
            }
        }

        var myKeys = (await db.EpochKeys.AsNoTracking()
                .Where(k => k.ConversationId == conversationId && k.UserId == me)
                .Select(k => new { k.Epoch, k.WrappedKey })
                .ToListAsync(cancellationToken))
            .ToDictionary(k => k.Epoch, k => k.WrappedKey);

        return new View(
            conversationId, direct is not null, folder?.Name ?? string.Empty,
            direct is null ? null : direct.UserA == me ? direct.UserB : direct.UserA,
            epoch, mine.WrappedKey, [.. keys.Select(k => k.UserId)], admins, unread,
            myEmail is not null && admins.Contains(myEmail, StringComparer.OrdinalIgnoreCase), me)
        {
            AdminIds = adminIds,
            Names = names,
            MyKeys = myKeys,
        };
    }

    /// <summary>
    /// The one conversation of the caller and <paramref name="counterpart"/>, created on first use with epoch 1's keys.
    /// Each member also gets a folder in their "My Messages" naming the other.
    /// </summary>
    public async Task<Guid> OpenDirectAsync(ModuleUser counterpart, IReadOnlyList<WrappedKey> keys, CancellationToken cancellationToken)
    {
        var me = Me;
        if (counterpart.Id == me)
        {
            throw MessengerErrors.Invalid("A conversation needs somebody else.");
        }

        var (a, b) = me.CompareTo(counterpart.Id) < 0 ? (me, counterpart.Id) : (counterpart.Id, me);
        if (await db.DirectConversations.FirstOrDefaultAsync(d => d.UserA == a && d.UserB == b, cancellationToken) is { } existing)
        {
            return existing.ConversationId;
        }

        await RequireKeysAsync([me, counterpart.Id], keys, cancellationToken);
        var myself = await caller.GetIdentityAsync(cancellationToken) ?? throw MessengerErrors.NotSignedIn();
        var repository = await RepositoryAsync(cancellationToken);

        var conversationId = Guid.Empty;
        await archive.InTransactionAsync(async () =>
        {
            conversationId = await archive.CreateDocumentAsync(repository, MessengerMasks.MessageGroup, $"direct-{a:N}-{b:N}", cancellationToken: cancellationToken);
            await archive.GrantAccessAsync(conversationId, me, ModuleAccess.Read, cancellationToken);
            await archive.GrantAccessAsync(conversationId, counterpart.Id, ModuleAccess.Read, cancellationToken);
            await PlaceInMyMessagesAsync(me, conversationId, counterpart.DisplayName, counterpart.Email, cancellationToken);
            await PlaceInMyMessagesAsync(counterpart.Id, conversationId, myself.DisplayName, myself.Email ?? string.Empty, cancellationToken);

            db.DirectConversations.Add(new DirectConversation { TenantId = caller.TenantId, UserA = a, UserB = b, ConversationId = conversationId });
            AddEpoch(conversationId, 1, keys);
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
        return conversationId;
    }

    /// <summary>A named group with the caller as its administrator and the given members, at epoch 1.</summary>
    public async Task<Guid> CreateGroupAsync(string name, IReadOnlyList<WrappedKey> keys, CancellationToken cancellationToken)
    {
        var me = Me;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw MessengerErrors.Invalid("A group needs a name.");
        }

        var members = keys.Select(k => k.UserId).Append(me).Distinct().ToList();
        await RequireKeysAsync(members, keys, cancellationToken);
        var myself = await caller.GetIdentityAsync(cancellationToken) ?? throw MessengerErrors.NotSignedIn();
        var repository = await RepositoryAsync(cancellationToken);

        var conversationId = Guid.Empty;
        await archive.InTransactionAsync(async () =>
        {
            conversationId = await archive.CreateDocumentAsync(repository, MessengerMasks.MessageGroup, name.Trim(), cancellationToken: cancellationToken);
            await archive.SetFieldListAsync(conversationId, MessengerMasks.AdminsField, [myself.Email ?? string.Empty], cancellationToken);
            foreach (var member in members)
            {
                await archive.GrantAccessAsync(conversationId, member, ModuleAccess.Read, cancellationToken);
            }

            AddEpoch(conversationId, 1, keys);
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
        return conversationId;
    }

    /// <summary>An administrator adds <paramref name="key"/>'s user at the CURRENT epoch: they read from joining on.</summary>
    public async Task AddMemberAsync(Guid conversationId, WrappedKey key, CancellationToken cancellationToken)
    {
        var view = await MemberViewAsync(conversationId, cancellationToken);
        await RequireAdminAsync(view, cancellationToken);
        if (view.IsDirect)
        {
            throw MessengerErrors.Invalid("A one-to-one conversation stays between two people; start a group instead.");
        }

        if (view.Members.Contains(key.UserId))
        {
            return;
        }

        if ((await archive.GetUserCertificatesAsync(key.UserId, cancellationToken)).Count == 0)
        {
            throw MessengerErrors.NoKey("This person");
        }

        await archive.InTransactionAsync(async () =>
        {
            await archive.GrantAccessAsync(conversationId, key.UserId, ModuleAccess.Read, cancellationToken);
            db.EpochKeys.Add(new EpochKey { TenantId = caller.TenantId, ConversationId = conversationId, Epoch = view.Epoch, UserId = key.UserId, WrappedKey = key.Key });
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// An administrator makes a member an administrator, or takes it back (also from themself). A group keeps at least one:
    /// the last administrator cannot step down, nor leave while others remain, until they have made someone else one.
    /// </summary>
    public async Task SetAdminAsync(Guid conversationId, Guid userId, bool admin, CancellationToken cancellationToken)
    {
        var view = await MemberViewAsync(conversationId, cancellationToken);
        await RequireAdminAsync(view, cancellationToken);
        if (view.IsDirect)
        {
            throw MessengerErrors.Invalid("A one-to-one conversation has no administrators.");
        }

        if (!view.Members.Contains(userId))
        {
            throw MessengerErrors.NotAMember();
        }

        if (view.AdminIds.Contains(userId) == admin)
        {
            return;
        }

        var person = await archive.GetUserAsync(userId, cancellationToken) ?? throw MessengerErrors.NotAMember();
        var admins = admin
            ? [.. view.Admins, person.Email]
            : view.Admins.Where(a => !a.Equals(person.Email, StringComparison.OrdinalIgnoreCase)).ToList();
        if (admins.Count == 0)
        {
            throw MessengerErrors.LastAdmin();
        }

        await archive.SetFieldListAsync(conversationId, MessengerMasks.AdminsField, admins, cancellationToken);
    }

    /// <summary>
    /// <paramref name="userId"/> leaves (themself) or is removed (by an administrator). A new epoch starts, with keys for
    /// every member who remains; the last one out removes the conversation, folder and all.
    /// </summary>
    public async Task RemoveMemberAsync(Guid conversationId, Guid userId, IReadOnlyList<WrappedKey> nextKeys, CancellationToken cancellationToken)
    {
        var view = await MemberViewAsync(conversationId, cancellationToken);
        if (userId != Me)
        {
            await RequireAdminAsync(view, cancellationToken);
        }

        if (!view.Members.Contains(userId))
        {
            return;
        }

        var remaining = view.Members.Where(m => m != userId).ToList();
        if (remaining.Count == 0)
        {
            await archive.InTransactionAsync(async () =>
            {
                db.Messages.RemoveRange(db.Messages.Where(m => m.ConversationId == conversationId));
                db.EpochKeys.RemoveRange(db.EpochKeys.Where(k => k.ConversationId == conversationId));
                db.KeyEpochs.RemoveRange(db.KeyEpochs.Where(e => e.ConversationId == conversationId));
                db.ReadReceipts.RemoveRange(db.ReadReceipts.Where(r => r.ConversationId == conversationId));
                db.DirectConversations.RemoveRange(db.DirectConversations.Where(d => d.ConversationId == conversationId));
                await db.SaveChangesAsync(cancellationToken);
                await archive.DeleteDocumentAsync(conversationId, cancellationToken);
            }, cancellationToken);
            return;
        }

        if (nextKeys.Count != remaining.Count || remaining.Any(m => nextKeys.All(k => k.UserId != m)))
        {
            throw MessengerErrors.KeysDoNotMatchMembers();
        }

        var leavingAdmin = view.AdminIds.Contains(userId);
        if (leavingAdmin && view.AdminIds.Count == 1 && !view.IsDirect)
        {
            throw MessengerErrors.LastAdmin();
        }

        var person = leavingAdmin ? await archive.GetUserAsync(userId, cancellationToken) : null;
        await archive.InTransactionAsync(async () =>
        {
            if (person is not null)
            {
                await archive.SetFieldListAsync(conversationId, MessengerMasks.AdminsField,
                    [.. view.Admins.Where(a => !a.Equals(person.Email, StringComparison.OrdinalIgnoreCase))], cancellationToken);
            }

            await archive.RevokeAccessAsync(conversationId, userId, cancellationToken);
            AddEpoch(conversationId, view.Epoch + 1, nextKeys);
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    /// <summary>The signed-in caller's name and e-mail.</summary>
    public Task<ModuleCallerIdentity?> IdentityAsync(CancellationToken cancellationToken) => caller.GetIdentityAsync(cancellationToken);

    /// <summary>The caller's view, or 404: a conversation one is not in does not exist for one.</summary>
    public async Task<View> MemberViewAsync(Guid conversationId, CancellationToken cancellationToken) =>
        await ViewAsync(conversationId, cancellationToken) ?? throw MessengerErrors.NotAMember();

    public async Task<int> CurrentEpochAsync(Guid conversationId, CancellationToken cancellationToken) =>
        await db.KeyEpochs.Where(e => e.ConversationId == conversationId).Select(e => (int?)e.Epoch).MaxAsync(cancellationToken) ?? 0;

    private static Task RequireAdminAsync(View view, CancellationToken cancellationToken) =>
        view.IAmAdmin ? Task.CompletedTask : throw MessengerErrors.NotAnAdmin();

    // Every member needs a key, and a certificate it could have been sealed to: nobody joins a conversation they cannot read.
    private async Task RequireKeysAsync(IReadOnlyList<Guid> members, IReadOnlyList<WrappedKey> keys, CancellationToken cancellationToken)
    {
        if (keys.Count != members.Count || members.Any(m => keys.Count(k => k.UserId == m) != 1) || keys.Any(k => k.Key.Length == 0))
        {
            throw MessengerErrors.KeysDoNotMatchMembers();
        }

        foreach (var member in members)
        {
            if ((await archive.GetUserCertificatesAsync(member, cancellationToken)).Count == 0)
            {
                throw MessengerErrors.NoKey(member == Me ? "You" : "A member");
            }
        }
    }

    private void AddEpoch(Guid conversationId, int epoch, IReadOnlyList<WrappedKey> keys)
    {
        db.KeyEpochs.Add(new KeyEpoch { TenantId = caller.TenantId, ConversationId = conversationId, Epoch = epoch, CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = Me });
        foreach (var key in keys)
        {
            db.EpochKeys.Add(new EpochKey { TenantId = caller.TenantId, ConversationId = conversationId, Epoch = epoch, UserId = key.UserId, WrappedKey = key.Key });
        }
    }

    // "My Messages/<counterpart>": where the person finds the chat in their own space. Skipped while they have no
    // personal space yet; the folder is placed when they do.
    private async Task PlaceInMyMessagesAsync(Guid userId, Guid conversationId, string counterpartName, string counterpartEmail, CancellationToken cancellationToken)
    {
        if (await archive.GetPersonalSpaceFolderAsync(userId, MessengerMasks.Messages, cancellationToken) is not { } myMessages
            || await ChatFolderAsync(userId, conversationId, cancellationToken) is not null)
        {
            return;
        }

        await archive.CreateDocumentAsync(myMessages, MessengerMasks.MessageUser, counterpartName,
            new Dictionary<string, string>
            {
                [MessengerMasks.CounterpartField] = counterpartEmail,
                [MessengerMasks.ConversationField] = conversationId.ToString(),
            }, cancellationToken);
    }

    /// <summary>A member's "My Messages/&lt;counterpart&gt;" folder of a one-to-one conversation, or null.</summary>
    public async Task<Guid?> ChatFolderAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        if (await archive.GetPersonalSpaceFolderAsync(userId, MessengerMasks.Messages, cancellationToken) is not { } myMessages)
        {
            return null;
        }

        return (await archive.GetChildrenAsync(myMessages, MessengerMasks.MessageUser, cancellationToken))
            .FirstOrDefault(f => f.Fields.GetValueOrDefault(MessengerMasks.ConversationField) == conversationId.ToString())?.Id;
    }

    private async Task<Guid> RepositoryAsync(CancellationToken cancellationToken) =>
        Guid.TryParse(await archive.GetSettingAsync(MessengerModule.RepositorySetting, cancellationToken), out var id)
            ? id
            : throw MessengerErrors.NoRepository();
}
