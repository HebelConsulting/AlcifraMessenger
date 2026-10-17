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
    /// <summary>The Messenger is never licensed (owner, 2026-10-17; core ADR 0944): no key to verify a licence with.</summary>
    public string LicenseVerifyKeyPem => string.Empty;

    /// <summary>
    /// Free and open source, wherever it runs: an administrator activates it without a licence file, and it never lapses
    /// (ABI 1.23, core ADR 0944). The standalone start (demo/up.sh) does that by itself.
    /// </summary>
    public bool RequiresLicence => false;

    public IReadOnlyList<ModuleMaskSeed> Masks => MessengerMasks.Seeds;

    /// <summary>
    /// The Messenger encrypts to users, so it licenses the core's certificate enrolment: while it is active, people can
    /// enrol the certificates their conversation keys are wrapped to.
    /// </summary>
    public bool LicensesCertificateEnrolment => true;

    private const string ManualResource = "SimplArchive.Messenger.Manual.pdf";

    /// <summary>
    /// The manual this module ships (ABI 1.5): the core files it into the tenant's manuals folder, and a new version
    /// whenever these bytes change, so a tenant reads the manual written for the code it runs.
    /// </summary>
    public ModuleManual? Manual { get; } = new(
        "SimplArchive-Messenger-Module-Manual.pdf",
        () => typeof(MessengerModule).Assembly.GetManifestResourceStream(ManualResource)
            ?? throw new InvalidOperationException($"The module's manual is not embedded as '{ManualResource}'."));

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
        services.AddScoped<Files>();
        services.AddScoped<Restores>();
    }
}
