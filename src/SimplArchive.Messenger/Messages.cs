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

    /// <summary>A page of messages, oldest first, after <paramref name="after"/> (exclusive); hidden-for-me left out.</summary>
    public async Task<IReadOnlyList<Message>> PageAsync(Guid conversationId, Guid? after, int limit, CancellationToken cancellationToken)
    {
        await conversations.MemberViewAsync(conversationId, cancellationToken);
        var me = conversations.Me;
        var hidden = db.HiddenMessages.Where(h => h.UserId == me).Select(h => h.MessageId);

        // Ordered in memory: SQLite (the tests' provider) cannot order by DateTimeOffset, and a conversation's page is small.
        var all = (await db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == conversationId && !hidden.Contains(m.Id))
                .ToListAsync(cancellationToken))
            .OrderBy(m => m.SentAt).ThenBy(m => m.Id)
            .ToList();
        var start = after is { } cursor ? all.FindIndex(m => m.Id == cursor) + 1 : 0;
        return [.. all.Skip(start).Take(Math.Clamp(limit, 1, 200))];
    }

    /// <summary>Posts a message the client sealed under the current epoch, with the id it bound into the ciphertext.</summary>
    public async Task<Message> SendAsync(
        Guid conversationId, Guid messageId, int epoch, byte[] ciphertext, Guid? replyToId, Guid? attachmentDocumentId,
        CancellationToken cancellationToken)
    {
        var view = await conversations.MemberViewAsync(conversationId, cancellationToken);
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

        // A message may carry only a file of its own conversation, never somebody else's document.
        if (attachmentDocumentId is { } file && !await files.BelongsAsync(conversationId, file, cancellationToken))
        {
            throw MessengerErrors.Invalid("The attachment is not a file of this conversation.");
        }

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
        await db.SaveChangesAsync(cancellationToken);
        await SignalOthersAsync(view, cancellationToken);
        return message;
    }

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
        var attachment = message.AttachmentDocumentId;
        (message.Ciphertext, message.DeletedAt, message.ConcurrencyToken, message.AttachmentDocumentId) = ([], DateTimeOffset.UtcNow, Guid.NewGuid(), null);
        await SaveTrackedAsync(cancellationToken);
        if (attachment is { } file)
        {
            await files.DeleteForEveryoneAsync(file, cancellationToken);   // the file goes with its message, references too
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
            if (message.AttachmentDocumentId is { } file)
            {
                await files.HideForMeAsync(conversationId, file, cancellationToken);   // my reference only
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
        var view = await conversations.MemberViewAsync(conversationId, cancellationToken);
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
