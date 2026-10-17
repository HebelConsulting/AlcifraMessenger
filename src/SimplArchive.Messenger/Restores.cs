using Microsoft.EntityFrameworkCore;
using SimplArchive.Messenger.ReadModel;
using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger;

/// <summary>
/// Bringing a backup's conversations in (core #1813). Nothing is decrypted here or on the way: the backup carries the
/// ciphertext as it was delivered and the restorer's own sealed copy of each epoch's key, which only their hardware key
/// opens. A restored conversation is read-only history in this slice; a second member restoring the same one joins it.
/// </summary>
public sealed class Restores(
    MessengerReadModelContext db, IModuleArchiveFacade archive, IModuleCallerContext caller, Conversations conversations, Files files)
{
    /// <summary>The most messages one import takes; a client sends a long conversation in batches.</summary>
    public const int MaxBatch = 200;

    /// <summary>A member as the backup names them: their id where the conversation was sealed.</summary>
    public sealed record Origin(Guid UserId, string Email, string DisplayName);

    /// <summary>One conversation of a backup: who was in it, and the restorer's own key per epoch.</summary>
    public sealed record Request(
        Guid OriginConversationId, string OriginInstance, string Name, IReadOnlyList<Origin> Members, Guid MyOriginUserId,
        IReadOnlyDictionary<int, byte[]> MyKeys)
    {
        /// <summary>A one-to-one chat where it was sealed (backups carry it; older restores said nothing, read as a group).</summary>
        public bool IsDirect { get; init; }
    }

    /// <summary>One message as it was stored, with its original id and sender id: what its ciphertext is bound to.</summary>
    public sealed record ImportedMessage(
        Guid Id, int Epoch, Guid SenderUserId, DateTimeOffset SentAt, DateTimeOffset? EditedAt, DateTimeOffset? DeletedAt,
        Guid? ReplyToId, Guid? AttachmentDocumentId, byte[] Ciphertext);

    /// <summary>
    /// Creates the restored conversation, or joins the one another member restored already: the restorer's keys and the
    /// members are added where missing, so restoring the same backup twice changes nothing.
    /// </summary>
    public async Task<Guid> BeginAsync(Request request, CancellationToken cancellationToken)
    {
        var me = conversations.Me;
        var myEmail = (await caller.GetIdentityAsync(cancellationToken))?.Email ?? throw MessengerErrors.NotSignedIn();

        // Only the backup's owner restores it: the member it names as its owner carries the caller's e-mail.
        if (request.Members.FirstOrDefault(m => m.UserId == request.MyOriginUserId) is not { } owner
            || !owner.Email.Equals(myEmail, StringComparison.OrdinalIgnoreCase))
        {
            throw MessengerErrors.NotYourBackup();
        }

        if (request.MyKeys.Count == 0 || request.MyKeys.Values.Any(k => k.Length == 0))
        {
            throw MessengerErrors.Invalid("A restored conversation needs the restorer's key for every epoch it holds.");
        }

        // Restoring onto the instance it came from, while it is still there: nothing to restore.
        if (await db.KeyEpochs.AnyAsync(e => e.ConversationId == request.OriginConversationId, cancellationToken)
            && !await db.RestoredConversations.AnyAsync(r => r.ConversationId == request.OriginConversationId, cancellationToken))
        {
            throw MessengerErrors.StillHere();
        }

        var existing = await db.RestoredConversations.FirstOrDefaultAsync(r => r.OriginConversationId == request.OriginConversationId, cancellationToken);
        var conversationId = existing?.ConversationId ?? Guid.Empty;
        await archive.InTransactionAsync(async () =>
        {
            if (existing is null)
            {
                var repository = await RepositoryAsync(cancellationToken);
                conversationId = await archive.CreateDocumentAsync(repository, MessengerMasks.MessageGroup,
                    await FreeNameAsync(repository, request.Name, cancellationToken), cancellationToken: cancellationToken);
                db.RestoredConversations.Add(new RestoredConversation
                {
                    ConversationId = conversationId,
                    TenantId = caller.TenantId,
                    OriginConversationId = request.OriginConversationId,
                    OriginInstance = request.OriginInstance.Length <= 500 ? request.OriginInstance : request.OriginInstance[..500],
                    RestoredAt = DateTimeOffset.UtcNow,
                    IsReadOnly = true,
                    IsDirect = request.IsDirect,
                });
            }

            await archive.GrantAccessAsync(conversationId, me, ModuleAccess.Read, cancellationToken);
            await AddMissingAsync(conversationId, request, me, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
        return conversationId;
    }

    /// <summary>
    /// Imports a batch of a restored conversation's messages, keeping their ids: one already here is skipped. Each must
    /// name a sender the backup lists and an epoch the restorer holds a key for, or the batch is refused.
    /// </summary>
    public async Task<(int Imported, int Skipped)> ImportAsync(Guid conversationId, IReadOnlyList<ImportedMessage> messages, CancellationToken cancellationToken)
    {
        var view = await conversations.MemberViewAsync(conversationId, cancellationToken);
        if (view.Restored is null)
        {
            throw MessengerErrors.Invalid("Only a restored conversation takes imported messages.");
        }

        if (messages.Count > MaxBatch)
        {
            throw MessengerErrors.Invalid($"At most {MaxBatch} messages are imported at once.");
        }

        var senders = view.Members.ToHashSet();
        foreach (var message in messages)
        {
            if (!senders.Contains(message.SenderUserId) || !view.MyKeys.ContainsKey(message.Epoch)
                || (message.Ciphertext.Length == 0 && message.DeletedAt is null))
            {
                throw MessengerErrors.Invalid($"Message {message.Id} names a sender or an epoch this backup does not hold, or carries nothing.");
            }

            if (message.AttachmentDocumentId is { } file && !await files.BelongsAsync(conversationId, file, cancellationToken))
            {
                throw MessengerErrors.Invalid("The attachment is not a file of this conversation.");
            }
        }

        var ids = messages.Select(m => m.Id).ToList();
        var present = (await db.Messages.Where(m => ids.Contains(m.Id)).Select(m => m.Id).ToListAsync(cancellationToken)).ToHashSet();
        var fresh = messages.Where(m => present.Add(m.Id)).ToList();   // present.Add: a duplicate within the batch counts once
        foreach (var m in fresh)
        {
            db.Messages.Add(new Message
            {
                Id = m.Id,
                TenantId = caller.TenantId,
                ConversationId = conversationId,
                Epoch = m.Epoch,
                SenderUserId = m.SenderUserId,
                SentAt = m.SentAt,
                EditedAt = m.EditedAt,
                DeletedAt = m.DeletedAt,
                ReplyToId = m.ReplyToId,
                AttachmentDocumentId = m.AttachmentDocumentId,
                Ciphertext = m.Ciphertext,
                ConcurrencyToken = Guid.NewGuid(),
                RestoredByUserId = conversations.Me,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return (fresh.Count, messages.Count - fresh.Count);
    }

    // The members, epochs and restorer's keys a backup brings that are not here yet; nothing present is changed.
    private async Task AddMissingAsync(Guid conversationId, Request request, Guid me, CancellationToken cancellationToken)
    {
        var known = (await db.OriginMembers.Where(o => o.ConversationId == conversationId).Select(o => o.OriginUserId).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var member in request.Members.Where(m => known.Add(m.UserId)))
        {
            db.OriginMembers.Add(new OriginMember
            {
                ConversationId = conversationId,
                OriginUserId = member.UserId,
                TenantId = caller.TenantId,
                Email = FolderNames.Clip(member.Email, 320),
                DisplayName = FolderNames.Clip(member.DisplayName, 200),
            });
        }

        var epochs = (await db.KeyEpochs.Where(e => e.ConversationId == conversationId).Select(e => e.Epoch).ToListAsync(cancellationToken)).ToHashSet();
        var mine = (await db.EpochKeys.Where(k => k.ConversationId == conversationId && k.UserId == me).Select(k => k.Epoch).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var (epoch, key) in request.MyKeys)
        {
            if (epochs.Add(epoch))
            {
                db.KeyEpochs.Add(new KeyEpoch { TenantId = caller.TenantId, ConversationId = conversationId, Epoch = epoch, CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = me });
            }

            if (mine.Add(epoch))
            {
                db.EpochKeys.Add(new EpochKey { TenantId = caller.TenantId, ConversationId = conversationId, Epoch = epoch, UserId = me, WrappedKey = key });
            }
        }
    }

    // "Crew (restored)", or "Crew (restored 2)" beside one restored before.
    private Task<string> FreeNameAsync(Guid repository, string name, CancellationToken cancellationToken)
    {
        var stem = FolderNames.Clip(string.IsNullOrWhiteSpace(name) ? "Conversation" : name.Trim(), 180);
        return FolderNames.FreeAsync(archive, repository, n => n == 1 ? $"{stem} (restored)" : $"{stem} (restored {n})", cancellationToken);
    }

    private async Task<Guid> RepositoryAsync(CancellationToken cancellationToken) =>
        Guid.TryParse(await archive.GetSettingAsync(MessengerModule.RepositorySetting, cancellationToken), out var id)
            ? id
            : throw MessengerErrors.NoRepository();
}
