namespace SimplArchive.Messenger.Client;

/// <summary>Restored history cannot continue here yet: these members are not here with an enrolled key.</summary>
public sealed class ContinuationBlockedException(IReadOnlyList<string> missing)
    : Exception($"Not here yet, with an enrolled key: {string.Join(", ", missing)}.")
{
    /// <summary>The members nobody here matches, by name.</summary>
    public IReadOnlyList<string> Missing { get; } = missing;
}
