using SimplArchive.Messenger.Client.Display;
namespace SimplArchive.Messenger.Views;

/// <summary>An opened photo, shown in the app from memory; Open with and Share hand it on as a sealed document is.</summary>
internal sealed class PhotoPage : ContentPage
{
    public PhotoPage(IFileHandoff handoff, byte[] content, string fileName, string contentType)
    {
        Title = fileName;
        ToolbarItems.Add(new ToolbarItem { Text = ViewText.Get("OpenWith"), Order = ToolbarItemOrder.Secondary, Command = new Command(async () => await handoff.OpenWithAsync(content, fileName, contentType)) });
        ToolbarItems.Add(new ToolbarItem { Text = ViewText.Get("Share"), Order = ToolbarItemOrder.Secondary, Command = new Command(async () => await handoff.ShareAsync(content, fileName, contentType)) });
        Content = new Image { Source = ImageSource.FromStream(() => new MemoryStream(content)), Aspect = Aspect.AspectFit };
    }
}
