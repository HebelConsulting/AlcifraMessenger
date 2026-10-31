namespace Alcifra.Messenger.Views;

/// <summary>An opened photo, shown in the app from memory; handing it on is the menu's, after a warning (<see cref="FileMenu"/>).</summary>
internal sealed class PhotoPage : ContentPage
{
    public PhotoPage(byte[] content, string fileName)
    {
        Title = fileName;
        Content = new Image { Source = ImageSource.FromStream(() => new MemoryStream(content)), Aspect = Aspect.AspectFit };
    }
}
