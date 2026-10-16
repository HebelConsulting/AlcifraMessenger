using Microsoft.Extensions.DependencyInjection;
using SimplArchive.Messenger.ReadModel;
using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger;

/// <summary>
/// The Messenger: conversations between the people of one SimplArchive installation, sealed to their hardware keys. The
/// server stores ciphertext only; a conversation key per epoch is wrapped to every member's enrolled certificates.
/// </summary>
public sealed class MessengerModule : IIndustryModule
{
    /// <summary>The setting naming the repository or folder conversations are filed under; chosen by an administrator.</summary>
    public const string RepositorySetting = "messenger.repositoryId";

    public string ModuleId => "messenger";

    public string DisplayName => "Messenger";

    public int AbiMajorVersion => ModuleAbiVersion.Major;

    public int AbiMinorVersion => ModuleAbiVersion.Minor;

    /// <summary>The vendor's public licence-verification key, the one every SimplArchive module trusts. Public by nature.</summary>
    public string LicenseVerifyKeyPem => """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE4HulTVVq8aoiJk9nwBpT78MW5Azi
        HuYu5r2EAzZYSCv2GR9PCVJ3vUe6YJvm60XgsrHxjJYuUF2PXV2LDFN3BA==
        -----END PUBLIC KEY-----
        """;

    public IReadOnlyList<ModuleMaskSeed> Masks => MessengerMasks.Seeds;

    /// <summary>
    /// The Messenger encrypts to users, so it licenses the core's certificate enrolment: while it is active, people can
    /// enrol the certificates their conversation keys are wrapped to.
    /// </summary>
    public bool LicensesCertificateEnrolment => true;

    public IReadOnlyList<ModuleSetting> Settings { get; } =
    [
        new(RepositorySetting, "Where conversations are filed",
            Description: "The id of the repository or folder group conversations and the shared folders of one-to-one chats "
                + "are created in. Its rights should be an administrator's: members get access to their own conversations only."),
    ];

    public IReadOnlyList<ModuleReadModelSet> ReadModels { get; } = [new ModuleReadModelSet(typeof(MessengerReadModelContext))];

    /// <summary>The Messenger's entries into the hypermedia graph: a client's only way to its addresses.</summary>
    public IReadOnlyList<ModuleRootLink> RootLinks { get; } =
    [
        new("messenger:conversations", "/" + Api.ConversationsController.Base, "GET"),
        new("messenger:people", "/" + Api.PeopleController.Base, "GET"),
    ];

    public void ConfigureServices(IServiceCollection services)
    {
        // Scoped: they depend on the read model and the facade, which are per request.
        services.AddScoped<Conversations>();
        services.AddScoped<Messages>();
    }
}
