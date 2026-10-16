using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger.Api;

// The Messenger's wire shapes. Binary values (keys, ciphertext) travel base64-encoded, as JSON does with byte arrays.

public sealed class ConversationResource : HypermediaResource
{
    public Guid Id { get; set; }
    public bool IsDirect { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? CounterpartUserId { get; set; }
    public int Epoch { get; set; }

    /// <summary>The caller's wrapped copy of the current epoch's conversation key, sealed to their certificates.</summary>
    public byte[] MyKey { get; set; } = [];

    /// <summary>The caller's copy of every epoch's key they hold, so messages from before a removal still open.</summary>
    public List<EpochKeyResource> MyKeys { get; set; } = [];

    /// <summary>A one-to-one chat's other person, by name: the chat's title.</summary>
    public string? CounterpartName { get; set; }

    /// <summary>Somebody left: follow <c>publish-epoch</c> with a new key for every member before sending.</summary>
    public bool NeedsNewEpoch { get; set; }

    public List<MemberResource> Members { get; set; } = [];
    public List<string> Admins { get; set; } = [];
    public int Unread { get; set; }

    /// <summary>The caller administers this group (adds and removes members); the server checks again on every change.</summary>
    public bool CanManageMembers { get; set; }
}

/// <summary>A member, with the address that removes them (or, for the caller, lets them leave).</summary>
public sealed class MemberResource : HypermediaResource
{
    public Guid UserId { get; set; }

    public bool IsAdmin { get; set; }

    /// <summary>The member's display name; empty for an account deactivated since.</summary>
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>One epoch's key, as the caller's sealed copy.</summary>
public sealed class EpochKeyResource
{
    public int Epoch { get; set; }

    public byte[] Key { get; set; } = [];
}

public sealed class ConversationListResource : HypermediaResource
{
    public List<ConversationResource> Items { get; set; } = [];
}

public sealed class MessageResource : HypermediaResource
{
    public Guid Id { get; set; }
    public int Epoch { get; set; }
    public Guid SenderUserId { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? ReplyToId { get; set; }
    public Guid? AttachmentDocumentId { get; set; }
    public byte[] Ciphertext { get; set; } = [];

    /// <summary>The tag an edit or a delete sends back as If-Match.</summary>
    public string ETag { get; set; } = string.Empty;
}

public sealed class MessagePageResource : HypermediaResource
{
    public List<MessageResource> Items { get; set; } = [];
}

public sealed class PersonResource : HypermediaResource
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public sealed class PeopleResource : HypermediaResource
{
    public List<PersonResource> Items { get; set; } = [];
}

public sealed class CertificatesResource : HypermediaResource
{
    /// <summary>Every usable certificate of the person: a conversation key is sealed to ALL of them.</summary>
    public List<string> CertificatesPem { get; set; } = [];
}

// Requests.

public sealed class KeyRequest
{
    public Guid UserId { get; set; }
    public byte[] Key { get; set; } = [];
}

public sealed class OpenDirectRequest
{
    public Guid CounterpartUserId { get; set; }
    public string CounterpartEmail { get; set; } = string.Empty;
    public List<KeyRequest> Keys { get; set; } = [];
}

public sealed class CreateGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public List<KeyRequest> Keys { get; set; } = [];
}

public sealed class AddMemberRequest
{
    public Guid UserId { get; set; }
    public byte[] Key { get; set; } = [];
}

public sealed class RemoveMemberRequest
{
    /// <summary>The next epoch's keys, one per member who remains, when an administrator removes someone; empty when a
    /// member leaves, because the leaver must not know the next key.</summary>
    public List<KeyRequest> NextKeys { get; set; } = [];
}

public sealed class PublishEpochRequest
{
    /// <summary>The next epoch's key, one per member.</summary>
    public List<KeyRequest> Keys { get; set; } = [];
}

public sealed class SendRequest
{
    /// <summary>The id the client bound into the ciphertext's associated data.</summary>
    public Guid Id { get; set; }
    public int Epoch { get; set; }
    public byte[] Ciphertext { get; set; } = [];
    public Guid? ReplyToId { get; set; }
    public Guid? AttachmentDocumentId { get; set; }
}

public sealed class EditRequest
{
    public int Epoch { get; set; }
    public byte[] Ciphertext { get; set; } = [];
}

public sealed class ReceiptRequest
{
    public DateTimeOffset ReadUpTo { get; set; }
}
