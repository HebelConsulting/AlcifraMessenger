using CommunityToolkit.Maui.Views;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// An opened video or sound, played in the app from memory (#27): the player reads the opened bytes through a stream, so
/// nothing of the file is written to storage. Closing the page stops it and lets go of the player.
/// </summary>
/// <remarks>The host registers the player once, with <c>UseMauiCommunityToolkitMediaElement</c> on its app builder.</remarks>
internal sealed class PlayerPage : ContentPage
{
    private readonly MediaElement _player;
    private readonly MemoryStream _content;

    public PlayerPage(byte[] content, string fileName)
    {
        Title = fileName;
        BackgroundColor = Colors.Black;
        _content = new MemoryStream(content, writable: false);
        _player = new MediaElement
        {
            Source = MediaSource.FromStream(_content),
            ShouldAutoPlay = true,
            ShouldShowPlaybackControls = true,
            Aspect = Aspect.AspectFit,
        };
        Content = _player;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (Navigation.NavigationStack.Contains(this))
        {
            return;   // covered (by the menu's warning, say), not closed: it plays on when it comes back
        }

        _player.Stop();
        _player.Handler?.DisconnectHandler();   // the platform's player goes with the page, not with the next collection
        _content.Dispose();
    }
}
