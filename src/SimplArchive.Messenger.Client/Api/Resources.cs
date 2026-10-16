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

    /// <summary>The members at the current epoch, each with the links the caller may follow for them.</summary>
    public List<Member> Members { get; set; } = [];

    /// <summary>The administrators' e-mail addresses, as the group's folder lists them.</summary>
    public List<string> Admins { get; set; } = [];

    /// <summary>Messages from others the caller has not read.</summary>
    public int Unread { get; set; }

    /// <summary>The caller administers this group.</summary>
    public bool CanManageMembers { get; set; }
}

/// <summary>A member, with <c>remove</c> (or leave, for the caller) and <c>make-admin</c>/<c>revoke-admin</c> where allowed.</summary>
public sealed class Member : Resource
{
    /// <summary>The member's user id.</summary>
    public Guid UserId { get; set; }

    /// <summary>The member administers the group.</summary>
    public bool IsAdmin { get; set; }
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
