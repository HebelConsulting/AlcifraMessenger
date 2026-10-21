using SimplArchive.Messenger.Client.Display;
#if !IOS
using CommunityToolkit.Maui.Views;
#endif

namespace SimplArchive.Messenger.Views;

/// <summary>
/// A video or a sound played from memory (#27): the bytes are handed to the platform's player as they are, so nothing of
/// the file is ever written to storage — sealed content opened with the key stays in memory, decrypted, and nowhere else.
/// The Messenger's player page shows one, and a host's document preview may show one too, so a document and a sent file
/// play the same way: which types play is <see cref="MediaTypes"/>' rule (and <see cref="FileOpening"/>'s).
/// </summary>
/// <remarks>
/// On Android the toolkit's <c>MediaElement</c> plays a stream over the bytes. On iOS the system player reads them through
/// a resource loader that answers its every request (the type, the length, each byte range, a seek, "to the end") from the
/// array (<see cref="MemoryRanges"/>). The host registers both once, with <see cref="PlayerHosting.UseMessengerPlayer"/>
/// on its app builder. Call <see cref="Stop"/> when the page showing it closes: the player goes with the page, not with
/// the next collection.
/// </remarks>
public sealed class MemoryPlayer : ContentView
{
#if IOS
    private readonly MemoryPlayerSurface _surface;
#else
    private readonly MediaElement _element;
    private readonly MemoryStream _stream;
#endif

    /// <param name="content">The opened file.</param>
    /// <param name="contentType">Its type, one that <see cref="MediaTypes.Plays"/>.</param>
    /// <exception cref="ArgumentException">The type is not one the app plays.</exception>
    public MemoryPlayer(byte[] content, string contentType)
    {
        ArgumentNullException.ThrowIfNull(content);
        var identifier = MediaTypes.IdentifierOf(contentType)
            ?? throw new ArgumentException($"{contentType} is not a type the app plays.", nameof(contentType));
        BackgroundColor = Colors.Black;
#if IOS
        Content = _surface = new MemoryPlayerSurface(content, identifier);
#else
        _ = identifier;   // Android's player reads the container from the bytes themselves
        _stream = new MemoryStream(content, writable: false);
        Content = _element = new MediaElement
        {
            Source = MediaSource.FromStream(_stream),
            ShouldAutoPlay = true,
            ShouldShowPlaybackControls = true,
            Aspect = Aspect.AspectFit,
        };
#endif
    }

    /// <summary>Stops playing and lets go of the platform's player. The player cannot play again afterwards.</summary>
    public void Stop()
    {
#if IOS
        _surface.Handler?.DisconnectHandler();
#else
        _element.Stop();
        _element.Handler?.DisconnectHandler();
        _stream.Dispose();
#endif
    }
}
