namespace Alcifra.Messenger.Views;

/// <summary>
/// An opened video or sound, played in the app from memory (#27): <see cref="MemoryPlayer"/> reads the opened bytes, so
/// nothing of the file is written to storage. Closing the page stops it and lets go of the player.
/// </summary>
/// <remarks>The host registers the player once, with <see cref="PlayerHosting.UseMessengerPlayer"/> on its app builder.</remarks>
internal sealed class PlayerPage : ContentPage
{
    private readonly MemoryPlayer _player;

    public PlayerPage(byte[] content, string fileName, string contentType)
    {
        Title = fileName;
        BackgroundColor = Colors.Black;
        Content = _player = new MemoryPlayer(content, contentType);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (Navigation.NavigationStack.Contains(this))
        {
            return;   // covered (by the menu's warning, say), not closed: it plays on when it comes back
        }

        _player.Stop();   // the platform's player goes with the page, not with the next collection
    }
}
