using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger.Tests;

/// <summary>
/// An archive in memory, with exactly what the Messenger asks of the core: folders and their fields, access entries,
/// purge, the user search, certificates and the personal-space folders. Everything else throws, so an unexpected call is
/// found by the tests rather than passing silently.
/// </summary>
internal sealed class FakeArchive : IModuleArchiveFacade
{
    public Dictionary<Guid, ModuleDocument> Documents { get; } = [];
    public HashSet<(Guid Document, Guid User)> Access { get; } = [];
    public List<Guid> Purged { get; } = [];
    public Dictionary<string, string> Settings { get; } = [];
    public List<ModuleUser> Users { get; } = [];
    public Dictionary<Guid, List<string>> Certificates { get; } = [];
    public Dictionary<Guid, Guid> MyMessages { get; } = [];

    public Task<ModuleDocument?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Documents.GetValueOrDefault(documentId));

    public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Settings.GetValueOrDefault(key));

    public Task<IReadOnlyList<ModuleDocument>> GetChildrenAsync(Guid parentDocumentId, Guid maskId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ModuleDocument>>([.. Documents.Values.Where(d => d.ParentId == parentDocumentId && d.MaskId == maskId)]);

    public Task<Guid> CreateDocumentAsync(Guid parentDocumentId, Guid maskId, string name, IReadOnlyDictionary<string, string>? fields = null, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        Documents[id] = new ModuleDocument(id, parentDocumentId, name, maskId, fields ?? new Dictionary<string, string>());
        return Task.FromResult(id);
    }

    public Task SetFieldListAsync(Guid documentId, string fieldName, IReadOnlyList<string> values, CancellationToken cancellationToken = default)
    {
        var document = Documents[documentId];
        Documents[documentId] = document with { FieldLists = new Dictionary<string, IReadOnlyList<string>>(document.FieldLists) { [fieldName] = values } };
        return Task.CompletedTask;
    }

    public Task InTransactionAsync(Func<Task> body, CancellationToken cancellationToken = default) => body();

    public Task GrantAccessAsync(Guid documentId, Guid userId, ModuleAccess access, CancellationToken cancellationToken = default)
    {
        Access.Add((documentId, userId));
        return Task.CompletedTask;
    }

    public Task RevokeAccessAsync(Guid documentId, Guid userId, CancellationToken cancellationToken = default)
    {
        Access.Remove((documentId, userId));
        return Task.CompletedTask;
    }

    public Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        Purged.Add(documentId);
        Documents.Remove(documentId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ModuleUser>> SearchUsersAsync(string term, int limit = 20, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ModuleUser>>([.. Users.Where(u => Certificates.ContainsKey(u.Id)
            && (u.Email.Contains(term, StringComparison.OrdinalIgnoreCase) || u.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))).Take(limit)]);

    public Task<IReadOnlyList<ReaderCertificate>> GetUserCertificatesAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReaderCertificate>>([.. Certificates.GetValueOrDefault(userId, []).Select(p => new ReaderCertificate(p, "card", DateTimeOffset.MaxValue))]);

    public Task<Guid?> GetPersonalSpaceFolderAsync(Guid userId, Guid folderMaskId, CancellationToken cancellationToken = default) =>
        Task.FromResult(MyMessages.TryGetValue(userId, out var folder) ? (Guid?)folder : null);

    public Task<byte[]?> GetDocumentContentAsync(Guid documentId, CancellationToken cancellationToken = default) => throw Unused();
    public Task<Guid> FileUploadAsync(string uploadId, Guid parentFolderId, Guid maskId, string name, string extension, IReadOnlyDictionary<string, string>? fields = null, Guid? replaceDocumentId = null, CancellationToken cancellationToken = default) => throw Unused();
    public Task<IReadOnlyList<ModuleDocument>> GetByMaskAsync(Guid maskId, CancellationToken cancellationToken = default) => throw Unused();
    public Task SetFieldsAsync(Guid documentId, IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken = default) => throw Unused();
    public Task CreateReferenceAsync(Guid targetDocumentId, Guid intoFolderId, CancellationToken cancellationToken = default) => throw Unused();
    public Task RenameDocumentAsync(Guid documentId, string name, CancellationToken cancellationToken = default) => throw Unused();
    public Task<Guid> StageContentAsync(Guid parentFolderId, Guid maskId, string name, byte[] content, string extension, DateTimeOffset expiresAt, IReadOnlyDictionary<string, string>? fields = null, Guid? replaceDocumentId = null, DateOnly? documentDate = null, TimeOnly? documentTime = null, CancellationToken cancellationToken = default) => throw Unused();
    public Task<Guid> CreateContentDocumentAsync(Guid parentFolderId, Guid maskId, string name, byte[] content, string extension, IReadOnlyDictionary<string, string>? fields = null, Guid? replaceDocumentId = null, DateOnly? documentDate = null, TimeOnly? documentTime = null, CancellationToken cancellationToken = default) => throw Unused();
    public Task ReplaceContentAsync(Guid documentId, byte[] content, CancellationToken cancellationToken = default) => throw Unused();
    public Task<bool> IsOfferedAsync(Guid resourceDocumentId, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken cancellationToken = default) => throw Unused();
    public Task<bool> IsFreeAsync(Guid resourceDocumentId, DateTimeOffset startsAt, DateTimeOffset endsAt, CancellationToken cancellationToken = default) => throw Unused();

    private static NotSupportedException Unused() => new("The Messenger does not ask this of the archive.");
}

/// <summary>The signed-in user, switchable between the people of a test.</summary>
internal sealed class FakeCaller(Guid tenantId) : IModuleCallerContext
{
    public Guid TenantId { get; } = tenantId;
    public Guid? UserId { get; set; }
    public Guid? ServiceAccountId => null;
    public Dictionary<Guid, (string Name, string Email)> People { get; } = [];

    public Task<bool> IsTenantAdminAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<ModuleCallerIdentity?> GetIdentityAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(UserId is { } id && People.TryGetValue(id, out var p) ? new ModuleCallerIdentity(p.Name, p.Email) : null);
}
