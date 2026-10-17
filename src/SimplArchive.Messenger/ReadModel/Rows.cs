namespace SimplArchive.Messenger.ReadModel;

// The Messenger's own tables (schema approved by the owner, 2026-10-16). Everything a person writes is CIPHERTEXT;
// the server never holds a conversation key in the clear. Only messages are concurrency-tracked: the other rows are
// append-only, created-and-deleted, or move only forward, so a lost update cannot happen to them.

/// <summary>The one one-to-one conversation of a pair: <see cref="UserA"/> sorts before <see cref="UserB"/>.</summary>
public sealed class DirectConversation
{
    public Guid TenantId { get; set; }
    public Guid UserA { get; set; }
    public Guid UserB { get; set; }

    /// <summary>The conversation's shared folder in the Messenger's repository.</summary>
    public Guid ConversationId { get; set; }
}

/// <summary>A key generation of a conversation; a new one whenever a member is removed or leaves. Append-only, but for
/// <see cref="ClosedAt"/>.</summary>
public sealed class KeyEpoch
{
    public Guid TenantId { get; set; }
    public Guid ConversationId { get; set; }
    public int Epoch { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByUserId { get; set; }

    /// <summary>
    /// When somebody LEFT under this epoch: its key is no longer for writing, because the leaver holds it. Nothing is
    /// sent until a remaining member publishes the next epoch — never the leaver, who would then know the key the group
    /// goes on with. Set once, never cleared; the next epoch is the answer.
    /// </summary>
    public DateTimeOffset? ClosedAt { get; set; }
}

/// <summary>
/// One member's copy of an epoch's conversation key: a CMS envelope addressed to all of that member's certificates.
/// Append-only; a joiner gets keys from the epoch they joined in, never earlier ones.
/// </summary>
public sealed class EpochKey
{
    public Guid TenantId { get; set; }
    public Guid ConversationId { get; set; }
    public int Epoch { get; set; }
    public Guid UserId { get; set; }
    public byte[] WrappedKey { get; set; } = [];
}

/// <summary>
/// A text message: AES-256-GCM ciphertext (nonce, ciphertext, tag) under its epoch's key, with the conversation, epoch,
/// message id and sender bound as associated data. Tracked: editing and deleting your own message honour If-Match.
/// </summary>
public sealed class Message
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ConversationId { get; set; }
    public int Epoch { get; set; }
    public Guid SenderUserId { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }

    /// <summary>Deleted for everyone: the ciphertext is gone, the row stays so the thread shows where it was.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? ReplyToId { get; set; }

    /// <summary>The Message Object a file message carries, in the archive.</summary>
    public Guid? AttachmentDocumentId { get; set; }

    /// <summary>
    /// Who brought this message in from a backup (core #1813); null for one sent here. A restored message keeps its
    /// original id and sender id, which its ciphertext is bound to, so the thread marks it rather than vouching for it.
    /// </summary>
    public Guid? RestoredByUserId { get; set; }

    public byte[] Ciphertext { get; set; } = [];
    public Guid ConcurrencyToken { get; set; }
}

/// <summary>"Delete for me": the message stays for everyone else. Created and deleted, never edited.</summary>
public sealed class HiddenMessage
{
    public Guid TenantId { get; set; }
    public Guid MessageId { get; set; }
    public Guid UserId { get; set; }
}

/// <summary>How far a member has read a conversation. Moves only forward, so two devices cannot undo each other.</summary>
public sealed class ReadReceipt
{
    public Guid TenantId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset ReadUpTo { get; set; }
}

/// <summary>
/// A conversation brought in from a backup (core #1813). Its folder here has a new id, but its messages are bound to the
/// one they were sealed under, so that is kept; a second member restoring the same conversation joins this one.
/// </summary>
/// <remarks>
/// Not concurrency-tracked, by the owner's exemption (2026-10-17): it is written by a restore, never edited by a person
/// from what they saw, so there is no precondition to carry; a second restore only adds keys and members beside it.
/// </remarks>
public sealed class RestoredConversation
{
    public Guid ConversationId { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>The conversation's id where it was sealed: what every message's ciphertext is bound to.</summary>
    public Guid OriginConversationId { get; set; }

    /// <summary>The installation the backup came from, as its address, for display.</summary>
    public string OriginInstance { get; set; } = string.Empty;

    public DateTimeOffset RestoredAt { get; set; }

    /// <summary>Archived history: nothing is sent, edited or rekeyed into it. Continuing it opens a live conversation instead.</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>A one-to-one chat where it was sealed: continuing it means the chat with that person here.</summary>
    public bool IsDirect { get; set; }

    /// <summary>
    /// The live conversation this history now sits before (core #1813): its members see it above that thread. Null until
    /// a member continued it.
    /// </summary>
    public Guid? ContinuedInConversationId { get; set; }
}

/// <summary>A member of a restored conversation as the backup names them: their id there, which the messages are bound to.</summary>
/// <remarks>Not concurrency-tracked, by the owner's exemption (2026-10-17): append-only, never changed once written.</remarks>
public sealed class OriginMember
{
    public Guid ConversationId { get; set; }
    public Guid OriginUserId { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
