using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger;

/// <summary>Every refusal the Messenger's API makes, defined once with its code, status and wording.</summary>
public static class MessengerErrors
{
    public static ModuleApiException NoRepository() => new("MESSENGER_NO_REPOSITORY", 409,
        "An administrator has not chosen where conversations are filed yet (the Messenger's repository setting).");

    public static ModuleApiException NotSignedIn() => new("MESSENGER_NOT_A_PERSON", 403,
        "Conversations are between people; this caller is not a signed-in user.");

    public static ModuleApiException NotAMember() => new("MESSENGER_NOT_A_MEMBER", 404,
        "There is no such conversation among yours.");

    public static ModuleApiException NotAnAdmin() => new("MESSENGER_NOT_AN_ADMIN", 403,
        "Only an administrator of this group can do that.");

    public static ModuleApiException NoKey(string who) => new("MESSENGER_NO_KEY", 409,
        $"{who} has no enrolled certificate, so a conversation key cannot be sealed to them.");

    public static ModuleApiException KeysDoNotMatchMembers() => new("MESSENGER_KEYS_MISMATCH", 400,
        "Every member needs exactly one wrapped key for the epoch, and only members.");

    public static ModuleApiException StaleEpoch(int current) => new("MESSENGER_STALE_EPOCH", 409,
        $"The conversation is at epoch {current}; encrypt with that epoch's key.");

    public static ModuleApiException NotYourMessage() => new("MESSENGER_NOT_YOUR_MESSAGE", 403,
        "Only the sender can change or delete a message for everyone.");

    public static ModuleApiException NoSuchMessage() => new("MESSENGER_NO_SUCH_MESSAGE", 404, "There is no such message here.");

    public static ModuleApiException MessageExists() => new("MESSENGER_MESSAGE_EXISTS", 409,
        "A message with this id exists already; a client chooses a fresh id for every message.");

    public static ModuleApiException IfMatchRequired() => new("IF_MATCH_REQUIRED", 428,
        "Send the If-Match of the message you are changing.");

    public static ModuleApiException EtagMismatch() => new("ETAG_MISMATCH", 412,
        "The message has been changed since you read it.");

    public static ModuleApiException RekeyNeeded() => new("MESSENGER_REKEY_NEEDED", 409,
        "Somebody left: a new key must be published before anything more is sent.");

    public static ModuleApiException NotClosed() => new("MESSENGER_EPOCH_OPEN", 409,
        "The current key is still in use; a new one is published when somebody leaves.");

    public static ModuleApiException LastAdmin() => new("MESSENGER_LAST_ADMIN", 409,
        "A group keeps at least one administrator: make someone else one first.");

    public static ModuleApiException Invalid(string what) => new("MESSENGER_INVALID", 400, what);

    public static ModuleApiException FileNotServed() => new("MESSENGER_FILE_NOT_SERVED", 409,
        "This installation does not hand out stored files directly (the strict tier), so the file cannot be downloaded here.");
}
