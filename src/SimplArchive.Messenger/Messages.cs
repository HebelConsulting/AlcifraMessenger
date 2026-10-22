using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SimplArchive.Messenger.ReadModel;
using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger;

/// <summary>
/// A conversation's messages: sealed text a member posts, edits or deletes, read page by page. The server checks that
/// the sender is a member, that the ciphertext is under the current epoch, and that only the sender changes a message.
/// </summary>
public sealed class Messages(
    MessengerReadModelContext db, Conversations conversations, Files files, IModuleNotices notices, ILogger<Messages> logger)
{
    /// <summary>What a member's phone shows: no sender and no text, because it reaches a lock screen.</summary>
    public const string SignalTitle = "New messages";

    public const string SignalBody = "Messenger";

    /// <summary>The most files one message carries (owner decision 2026-10-21: the composer's chips are one message).</summary>
    public const int MaxFiles = 10;

    /// <summary>A page of messages, oldest first, after <paramref name="after"/> (exclusive); hidden-for-me left out.</summary>
    public async Task<(Conversations.View View, IReadOnlyList<Message> Page)> PageAsync(Guid conversationId, Guid? after, int limit, CancellationToken cancellationToken)
    {
        var view = await conversations.MemberViewAsync(conversationId, cancellationToken);
        var me = conversations.Me;
        var hidden = db.HiddenMessages.Where(h => h.UserId == me).Select(h => h.MessageId);

        // Exactly the epochs the caller holds a key for: what came before they joined, while they were away, or after they
        // left is not theirs to list — and for a member throughout, that is simply everything since they joined.
        var epochs = view.MyKeys.Keys.ToList();

        // Ordered in memory: SQLite (the tests' provider) cannot order by DateTimeOffset, and a conversation's page is small.
        var all = (await db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == conversationId && epochs.Contains(m.Epoch) && !hidden.Contains(m.Id))
                .ToListAsync(cancellationToken))
            .OrderBy(m => m.SentAt).ThenBy(m => m.Id)
            .ToList();
        var start = after is { } cursor ? all.FindIndex(m => m.Id == cursor) + 1 : 0;
        return (view, [.. all.Skip(start).Take(Math.Clamp(limit, 1, 200))]);
    }

    /// <summary>
    /// Every file each of <paramref name="messages"/> carries, in order: its first (<see cref="Message.AttachmentDocumentId"/>)
    /// and the further ones; a message without files is absent.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> FilesOfAsync(IReadOnlyCollection<Message> messages, CancellationToken cancellationToken)
    {
        var ids = messages.Where(m => m.AttachmentDocumentId is not null).Select(m => m.Id).ToList();
        var further = ids.Count == 0 ? [] : await db.MessageFiles.AsNoTracking().Where(f => ids.Contains(f.MessageId)).ToListAsync(cancellationToken);
        return messages.Where(m => m.AttachmentDocumentId is not null).ToDictionary(
            m => m.Id,
            m => (IReadOnlyList<Guid>)[m.AttachmentDocumentId!.Value, .. further.Where(f => f.MessageId == m.Id).OrderBy(f => f.Position).Select(f => f.DocumentId)]);
    }

    /// <summary>
    /// Posts a message the client sealed under the current epoch, with the id it bound into the ciphertext, and the files
    /// it carries in order (none, one, or up to <see cref="MaxFiles"/>): the first is the message's attachment, as a client
    /// that knows one file per message reads it; the others are further files.
    /// </summary>
    public async Task<Message> SendAsync(
        Guid conversationId, Guid messageId, int epoch, byte[] ciphertext, Guid? replyToId, IReadOnlyList<Guid> attachmentDocumentIds,
        CancellationToken cancellationToken)
    {
        var view = await conversations.WritableViewAsync(conversationId, cancellationToken);
        if (view.NeedsNewEpoch)
        {
            throw MessengerErrors.RekeyNeeded();
        }

        if (epoch != view.Epoch)
        {
            throw MessengerErrors.StaleEpoch(view.Epoch);
        }

        if (ciphertext.Length == 0)
        {
            throw MessengerErrors.Invalid("A message needs its ciphertext.");
        }

        if (await db.Messages.AnyAsync(m => m.Id == messageId, cancellationToken))
        {
            throw MessengerErrors.MessageExists();
        }

        await RequireFilesAsync(conversationId, attachmentDocumentIds, cancellationToken);
        Guid? attachmentDocumentId = attachmentDocumentIds.Count > 0 ? attachmentDocumentIds[0] : null;

        var message = new Message
        {
            Id = messageId,
            TenantId = (await db.KeyEpochs.FirstAsync(e => e.ConversationId == conversationId, cancellationToken)).TenantId,
            ConversationId = conversationId,
            Epoch = epoch,
            SenderUserId = conversations.Me,
            SentAt = DateTimeOffset.UtcNow,
            ReplyToId = replyToId,
            AttachmentDocumentId = attachmentDocumentId,
            Ciphertext = ciphertext,
            ConcurrencyToken = Guid.NewGuid(),
        };
        db.Messages.Add(message);
        for (var position = 1; position < attachmentDocumentIds.Count; position++)
        {
            db.MessageFiles.Add(new MessageFile
            {
                MessageId = messageId, Position = position, TenantId = message.TenantId, DocumentId = attachmentDocumentIds[position],
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await SignalOthersAsync(view, cancellationToken);
        return message;
    }

    // A message carries only files of its own conversation, never somebody else's document, each once, and none another
    // message carries already.
    internal async Task RequireFilesAsync(Guid conversationId, IReadOnlyList<Guid> documentIds, CancellationToken cancellationToken)
    {
        if (documentIds.Count > MaxFiles || documentIds.Distinct().Count() != documentIds.Count)
        {
            throw MessengerErrors.Invalid($"A message carries at most {MaxFiles} files, each once.");
        }

        foreach (var file in documentIds)
        {
            if (!await files.BelongsAsync(conversationId, file, cancellationToken))
            {
                throw MessengerErrors.Invalid("The attachment is not a file of this conversation.");
            }
        }

        if (documentIds.Count > 0
            && (await db.Messages.AnyAsync(m => m.AttachmentDocumentId != null && documentIds.Contains(m.AttachmentDocumentId.Value), cancellationToken)
                || await db.MessageFiles.AnyAsync(f => documentIds.Contains(f.DocumentId), cancellationToken)))
        {
            throw MessengerErrors.Invalid("A file is carried by one message only.");
        }
    }

    // Every file the message carries: its first, then the further ones.
    private async Task<List<Guid>> AllFilesAsync(Message message, CancellationToken cancellationToken) =>
        message.AttachmentDocumentId is not { } first ? []
            : [first, .. await db.MessageFiles.Where(f => f.MessageId == message.Id).OrderBy(f => f.Position).Select(f => f.DocumentId).ToListAsync(cancellationToken)];

    // After the message is stored: a signal that fails must not make a sent message look unsent, so it is logged, not
    // thrown. The core folds repeats into the member's unread signal, so a burst pushes their phone once.
    private async Task SignalOthersAsync(Conversations.View view, CancellationToken cancellationToken)
    {
        foreach (var member in view.Members.Where(m => m != view.Me))
        {
            try
            {
                await notices.SignalAsync(new ModuleSignal(member, view.Id, SignalTitle, SignalBody), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Could not signal member {UserId} about conversation {ConversationId}", member, view.Id);
            }
        }
    }

    /// <summary>The sender replaces the text, under the current epoch, provided nobody changed it since they read it.</summary>
    public async Task<Message> EditAsync(Guid conversationId, Guid messageId, Guid? ifMatch, int epoch, byte[] ciphertext, CancellationToken cancellationToken)
    {
        var (message, view) = await OwnAsync(conversationId, messageId, ifMatch, cancellationToken);
        if (view.NeedsNewEpoch)
        {
            throw MessengerErrors.RekeyNeeded();
        }

        if (epoch != view.Epoch)
        {
            throw MessengerErrors.StaleEpoch(view.Epoch);
        }

        (message.Epoch, message.Ciphertext, message.EditedAt, message.ConcurrencyToken) = (epoch, ciphertext, DateTimeOffset.UtcNow, Guid.NewGuid());
        await SaveTrackedAsync(cancellationToken);
        return message;
    }

    /// <summary>The sender deletes the message for everyone: the text is gone, the place in the thread stays.</summary>
    public async Task DeleteForEveryoneAsync(Guid conversationId, Guid messageId, Guid? ifMatch, CancellationToken cancellationToken)
    {
        var (message, _) = await OwnAsync(conversationId, messageId, ifMatch, cancellationToken);
        var carried = await AllFilesAsync(message, cancellationToken);
        (message.Ciphertext, message.DeletedAt, message.ConcurrencyToken, message.AttachmentDocumentId) = ([], DateTimeOffset.UtcNow, Guid.NewGuid(), null);
        db.MessageFiles.RemoveRange(db.MessageFiles.Where(f => f.MessageId == message.Id));
        await SaveTrackedAsync(cancellationToken);
        foreach (var file in carried)
        {
            await files.DeleteForEveryoneAsync(file, cancellationToken);   // the files go with their message, references too
        }
    }

    /// <summary>Hides a message for the caller only.</summary>
    public async Task HideForMeAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken)
    {
        await conversations.MemberViewAsync(conversationId, cancellationToken);
        var me = conversations.Me;
        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == conversationId, cancellationToken)
            ?? throw MessengerErrors.NoSuchMessage();
        if (!await db.HiddenMessages.AnyAsync(h => h.MessageId == messageId && h.UserId == me, cancellationToken))
        {
            db.HiddenMessages.Add(new HiddenMessage { TenantId = message.TenantId, MessageId = messageId, UserId = me });
            await db.SaveChangesAsync(cancellationToken);
            foreach (var file in await AllFilesAsync(message, cancellationToken))
            {
                await files.HideForMeAsync(conversationId, file, cancellationToken);   // my references only
            }
        }
    }

    /// <summary>Moves the caller's read marker forward; an older value from another device changes nothing.</summary>
    public async Task MarkReadAsync(Guid conversationId, DateTimeOffset readUpTo, CancellationToken cancellationToken)
    {
        await conversations.MemberViewAsync(conversationId, cancellationToken);
        var me = conversations.Me;
        var receipt = await db.ReadReceipts.FirstOrDefaultAsync(r => r.ConversationId == conversationId && r.UserId == me, cancellationToken);
        if (receipt is null)
        {
            var tenantId = (await db.KeyEpochs.FirstAsync(e => e.ConversationId == conversationId, cancellationToken)).TenantId;
            db.ReadReceipts.Add(new ReadReceipt { TenantId = tenantId, ConversationId = conversationId, UserId = me, ReadUpTo = readUpTo });
        }
        else if (readUpTo > receipt.ReadUpTo)
        {
            receipt.ReadUpTo = readUpTo;
        }

        await db.SaveChangesAsync(cancellationToken);
        await notices.ClearSignalsAsync(me, conversationId, cancellationToken);   // caught up: the next message pushes again
    }

    private async Task<(Message Message, Conversations.View View)> OwnAsync(Guid conversationId, Guid messageId, Guid? ifMatch, CancellationToken cancellationToken)
    {
        var view = await conversations.WritableViewAsync(conversationId, cancellationToken);
        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == conversationId, cancellationToken)
            ?? throw MessengerErrors.NoSuchMessage();
        if (message.SenderUserId != conversations.Me)
        {
            throw MessengerErrors.NotYourMessage();
        }

        if (message.DeletedAt is not null)
        {
            throw MessengerErrors.NoSuchMessage();
        }

        // The precondition is the tag the sender SAW: required, and compared through EF so a change in between fails.
        var token = ifMatch ?? throw MessengerErrors.IfMatchRequired();
        db.Entry(message).Property(m => m.ConcurrencyToken).OriginalValue = token;
        return (message, view);
    }

    private async Task SaveTrackedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw MessengerErrors.EtagMismatch();
        }
    }
}
