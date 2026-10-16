using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger;

/// <summary>
/// The Messenger's kinds of document. Structure lives in the archive: a "Messages" folder in every personal space,
/// one folder per one-to-one chat naming the counterpart, one folder per group conversation (its members are its access
/// list), and the files people send. Text, key epochs and receipts live in the module's own tables.
/// </summary>
public static class MessengerMasks
{
    /// <summary>"My Messages", provisioned by the core in every user's personal space.</summary>
    public static readonly Guid Messages = Guid.Parse("3e55a6e0-5c3a-4d6b-9a10-6d2f0b1c0001");

    /// <summary>A one-to-one chat in a user's "My Messages", naming the counterpart.</summary>
    public static readonly Guid MessageUser = Guid.Parse("3e55a6e0-5c3a-4d6b-9a10-6d2f0b1c0002");

    /// <summary>A conversation's own folder in the Messenger's repository: a group, or the shared folder of a pair.</summary>
    public static readonly Guid MessageGroup = Guid.Parse("3e55a6e0-5c3a-4d6b-9a10-6d2f0b1c0003");

    /// <summary>A file sent in a conversation: a CMS envelope sealed to the members' certificates.</summary>
    public static readonly Guid MessageObject = Guid.Parse("3e55a6e0-5c3a-4d6b-9a10-6d2f0b1c0004");

    /// <summary>The field naming a one-to-one chat's counterpart, by e-mail.</summary>
    public const string CounterpartField = "Counterpart";

    /// <summary>The field listing a group's administrators, by e-mail.</summary>
    public const string AdminsField = "Admins";

    /// <summary>The field naming who sent a file, by e-mail.</summary>
    public const string SenderField = "Sender";

    public static IReadOnlyList<ModuleMaskSeed> Seeds { get; } =
    [
        new(Messages, "Messages", IsFolderMask: true, IsBookable: false, Fields: [],
            AdmitsOnlyDeclaredChildren: true, AdmittedChildren: [MessageUser])
        {
            PersonalSpaceFolderName = "My Messages",
        },
        new(MessageUser, "Message User", IsFolderMask: true, IsBookable: false,
            Fields: [new ModuleFieldSeed(CounterpartField, "EmailAddress")]),
        new(MessageGroup, "Message Group", IsFolderMask: true, IsBookable: false,
            Fields: [new ModuleFieldSeed(AdminsField, "EmailAddress", IsList: true)]),
        new(MessageObject, "Message Object", IsFolderMask: false, IsBookable: false,
            Fields: [new ModuleFieldSeed(SenderField, "EmailAddress")])
        {
            // Sealed to its readers, so the core can neither search nor preview it, and the module owns its lifetime.
            ContentSegment = "messenger",
            Exemptions = ModuleMaskExemptions.All,
        },
    ];
}
