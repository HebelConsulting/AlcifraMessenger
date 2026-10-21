namespace SimplArchive.Messenger.Views;

/// <summary>Where <see cref="MemoryPlayer"/> shows the system player on iOS; its handler is <see cref="MemoryPlayerHandler"/>.</summary>
internal sealed class MemoryPlayerSurface(byte[] content, string identifier) : View
{
    /// <summary>The opened file.</summary>
    public byte[] Bytes { get; } = content;

    /// <summary>Its uniform type identifier, which the system player is told.</summary>
    public string Identifier { get; } = identifier;
}
