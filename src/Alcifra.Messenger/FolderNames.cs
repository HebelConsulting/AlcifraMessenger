using SimplArchive.ModuleAbi;

namespace Alcifra.Messenger;

/// <summary>A group's folder name that is free among its siblings: folder names are unique beside each other.</summary>
internal static class FolderNames
{
    /// <summary>
    /// The first of <c>candidate(1)</c>, <c>candidate(2)</c>, … not taken by a conversation folder in
    /// <paramref name="repository"/>.
    /// </summary>
    public static async Task<string> FreeAsync(IModuleArchiveFacade archive, Guid repository, Func<int, string> candidate, CancellationToken cancellationToken)
    {
        var taken = (await archive.GetChildrenAsync(repository, MessengerMasks.MessageGroup, cancellationToken))
            .Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var n = 1;
        while (taken.Contains(candidate(n)))
        {
            n++;
        }

        return candidate(n);
    }

    public static string Clip(string value, int max) => value.Length <= max ? value : value[..max];
}
