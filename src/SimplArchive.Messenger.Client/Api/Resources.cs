namespace SimplArchive.Messenger.Client.Api;

// The Messenger's wire shapes as a client reads them. Binary values (keys, ciphertext) travel base64-encoded.

/// <summary>An address the server advertised: follow it by its <see cref="Rel"/>, never by composing a path.</summary>
public sealed record Link(string Rel, string Href, string Method);

/// <summary>Anything the server returns with links.</summary>
public abstract class Resource
{
    /// <summary>What the caller may do from here; a missing rel means "not available to you, here, now".</summary>
    public List<Link> Links { get; set; } = [];

    /// <summary>The link with <paramref name="rel"/>, or null.</summary>
    public Link? LinkOf(string rel) => Links.FirstOrDefault(l => l.Rel == rel);

    /// <summary>Whether the server offers <paramref name="rel"/> here.</summary>
    public bool Offers(string rel) => LinkOf(rel) is not null;
}

/// <summary>The caller's conversations, with the links that open a one-to-one chat or create a group.</summary>
public sealed class ConversationList : Resource
{
    /// <summary>The conversations, as the caller sees each.</summary>
    public List<Conversation> Items { get; set; } = [];
}

/// <summary>One conversation as a member sees it.</summary>
public sealed class Conversation : Resource
{
    /// <summary>The conversation's id, bound into every message's associated data.</summary>
    public Guid Id { get; set; }

    /// <summary>A one-to-one chat rather than a group.</summary>
    public bool IsDirect { get; set; }

    /// <summary>A group's name; a one-to-one chat's shared folder name, not for display.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The other person of a one-to-one chat.</summary>
    public Guid? CounterpartUserId { get; set; }

    /// <summary>The current key epoch: new messages are sealed under it.</summary>
    public int Epoch { get; set; }

    /// <summary>The caller's copy of the current epoch's key, sealed to their certificates.</summary>
    public byte[] MyKey { get; set; } = [];

    /// <summary>The caller's copy of every epoch's key they hold: a message opens with the key of its own epoch.</summary>
    public List<EpochKey> MyKeys { get; set; } = [];

    /// <summary>A one-to-one chat's other person, by name: the chat's title.</summary>
    public string? CounterpartName { get; set; }

    /// <summary>Somebody left: publish a new key for every member (<see cref="MessengerApi.PublishEpochAsync"/>) before sending.</summary>
    public bool NeedsNewEpoch { get; set; }

    /// <summary>The members at the current epoch, each with the links the caller may follow for them.</summary>
    public List<Member> Members { get; set; } = [];

    /// <summary>The administrators' e-mail addresses, as the group's folder lists them.</summary>
    public List<string> Admins { get; set; } = [];

    /// <summary>Messages from others the caller has not read.</summary>
    public int Unread { get; set; }

    /// <summary>The caller administers this group.</summary>
    public bool CanManageMembers { get; set; }

    /// <summary>
    /// The conversation id every message is bound to: <see cref="Id"/>, or for a restored conversation (core #1813) the
    /// id it had where its messages were sealed. Use <see cref="SealedUnder"/>, which also answers an older server.
    /// </summary>
    public Guid SealedUnderId { get; set; }

    /// <summary>The id the caller's own messages carry here: their user id, or in a restored conversation their id there.</summary>
    public Guid MyUserId { get; set; }

    /// <summary>Brought in from a backup.</summary>
    public bool Restored { get; set; }

    /// <summary>The installation a restored conversation came from.</summary>
    public string? RestoredFrom { get; set; }

    /// <summary>Archived history: readable, not continued — nothing is sent, edited or rekeyed.</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>The caller left (or was removed): they read the stretch they were a member, as history.</summary>
    public bool Left { get; set; }

    /// <summary>The id bound into every message's associated data.</summary>
    public Guid SealedUnder => SealedUnderId == Guid.Empty ? Id : SealedUnderId;
}

/// <summary>A member, with <c>remove</c> (or leave, for the caller) and <c>make-admin</c>/<c>revoke-admin</c> where allowed.</summary>
public sealed class Member : Resource
{
    /// <summary>The member's user id.</summary>
    public Guid UserId { get; set; }

    /// <summary>The member administers the group.</summary>
    public bool IsAdmin { get; set; }

    /// <summary>The member's display name; empty for an account deactivated since.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The member has read every message sent up to this moment; null while they have read nothing.</summary>
    public DateTimeOffset? ReadUpTo { get; set; }

    /// <summary>The member's e-mail: what a backup names them by.</summary>
    public string Email { get; set; } = string.Empty;
}

/// <summary>The caller's sealed copy of one epoch's conversation key.</summary>
public sealed class EpochKey
{
    /// <summary>The epoch.</summary>
    public int Epoch { get; set; }

    /// <summary>The key, sealed to the caller's certificates.</summary>
    public byte[] Key { get; set; } = [];
}

/// <summary>A page of messages, oldest first, with <c>send</c> and, while there may be more, <c>next</c>.</summary>
public sealed class MessagePage : Resource
{
    /// <summary>The messages on this page.</summary>
    public List<Message> Items { get; set; } = [];
}

/// <summary>A sealed message as the server stores it.</summary>
public sealed class Message : Resource
{
    /// <summary>The id the sender bound into the ciphertext.</summary>
    public Guid Id { get; set; }

    /// <summary>The epoch whose key sealed it.</summary>
    public int Epoch { get; set; }

    /// <summary>Who sent it.</summary>
    public Guid SenderUserId { get; set; }

    /// <summary>When the server received it.</summary>
    public DateTimeOffset SentAt { get; set; }

    /// <summary>When the sender last replaced the text.</summary>
    public DateTimeOffset? EditedAt { get; set; }

    /// <summary>When the sender deleted it for everyone; the ciphertext is then empty.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>The message this one replies to.</summary>
    public Guid? ReplyToId { get; set; }

    /// <summary>The sealed file it carries, reached by the <c>attachment</c> link.</summary>
    public Guid? AttachmentDocumentId { get; set; }

    /// <summary>The sealed text (format version 1).</summary>
    public byte[] Ciphertext { get; set; } = [];

    /// <summary>Brought in from a backup: as trustworthy as whoever restored it, so a thread marks it.</summary>
    public bool Restored { get; set; }

    /// <summary>The tag an edit or a delete sends back as If-Match: what the caller saw.</summary>
    public string ETag { get; set; } = string.Empty;
}

/// <summary>People the caller may start a conversation with: users with an enrolled key.</summary>
public sealed class PeopleList : Resource
{
    /// <summary>The people found.</summary>
    public List<Person> Items { get; set; } = [];
}

/// <summary>A person, with the <c>certificates</c> their conversation keys are sealed to.</summary>
public sealed class Person : Resource
{
    /// <summary>The person's user id.</summary>
    public Guid Id { get; set; }

    /// <summary>Their display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Their e-mail address.</summary>
    public string Email { get; set; } = string.Empty;
}

/// <summary>Every usable certificate of a person: a conversation key is sealed to all of them.</summary>
public sealed class Certificates : Resource
{
    /// <summary>The certificates, PEM-encoded.</summary>
    public List<string> CertificatesPem { get; set; } = [];
}

/// <summary>A begun upload: PUT the sealed file to <see cref="Url"/>, then follow <c>complete</c>.</summary>
public sealed class Upload : Resource
{
    /// <summary>Opaque: names the upload to the server.</summary>
    public string UploadId { get; set; } = string.Empty;

    /// <summary>The presigned address. A credential for its lifetime: never log it.</summary>
    public Uri Url { get; set; } = new("about:blank");

    /// <summary>When the address stops accepting the PUT.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>The largest file accepted.</summary>
    public long MaxBytes { get; set; }
}

/// <summary>A filed file, which a message then names.</summary>
public sealed class FiledFile : Resource
{
    /// <summary>The file's document id, sent as a message's attachment.</summary>
    public Guid DocumentId { get; set; }
}

/// <summary>A member's copy of a conversation key, sealed to their certificates.</summary>
public sealed record WrappedKey(Guid UserId, byte[] Key);

/// <summary>What one import of restored messages did.</summary>
public sealed class ImportResult
{
    /// <summary>Messages added.</summary>
    public int Imported { get; set; }

    /// <summary>Already there: from an earlier restore of the same backup, or another member's.</summary>
    public int Skipped { get; set; }
}

/// <summary>A member of a conversation as a backup names them: their id where its messages were sealed.</summary>
public sealed record BackupMember(Guid UserId, string Email, string DisplayName);

/// <summary>A message as a backup holds it, with the id and sender id its ciphertext is bound to.</summary>
public sealed record BackupMessage(
    Guid Id, int Epoch, Guid SenderUserId, DateTimeOffset SentAt, DateTimeOffset? EditedAt, DateTimeOffset? DeletedAt,
    Guid? ReplyToId, Guid? AttachmentDocumentId, byte[] Ciphertext);

/// <summary>Who a restored conversation's members are here, and whether it can continue as a live conversation.</summary>
public sealed class Continuation : Resource
{
    /// <summary>A one-to-one chat: it continues as the chat with that person.</summary>
    public bool IsDirect { get; set; }

    /// <summary>The name a continued group gets: the original one.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The chat with that person already started here, which continuing joins without a new key.</summary>
    public Guid? ExistingConversationId { get; set; }

    /// <summary>The old members, each with who they are here.</summary>
    public List<ContinuationMember> Members { get; set; } = [];

    /// <summary>Old members nobody here matches: while any are, it stays history.</summary>
    public List<string> Missing { get; set; } = [];

    /// <summary>Every member is here.</summary>
    public bool CanContinue { get; set; }
}

/// <summary>An old member, and who they are here, with the certificates the live conversation's key is sealed to.</summary>
public sealed class ContinuationMember : Resource
{
    /// <summary>Their id where the history was sealed.</summary>
    public Guid OriginUserId { get; set; }

    /// <summary>The e-mail they are matched by.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Their name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Their user id here; null when nobody matches.</summary>
    public Guid? UserId { get; set; }
}
