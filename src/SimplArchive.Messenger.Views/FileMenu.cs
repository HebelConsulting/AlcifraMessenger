using SimplArchive.Messenger.Client.Display;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// Handing an opened file on (#27): Open with… and Share sit in the page's menu (⋮), never behind a tap, and each first
/// warns that the file leaves the app unsealed and is no longer protected by it. Where the tenant keeps files in the app
/// the menu offers neither.
/// </summary>
internal static class FileMenu
{
    /// <summary>Adds Open with… and Share to <paramref name="page"/>'s menu, where <paramref name="opening"/> lets the file leave the app.</summary>
    public static void Offer(ContentPage page, IFileHandoff handoff, FileOpening opening, byte[] content, string fileName)
    {
        if (!opening.MayLeaveTheApp)
        {
            return;
        }

        page.ToolbarItems.Add(Item(page, "OpenWith", () => handoff.OpenWithAsync(content, fileName, opening.ContentType)));
        page.ToolbarItems.Add(Item(page, "Share", () => handoff.ShareAsync(content, fileName, opening.ContentType)));
    }

    private static ToolbarItem Item(ContentPage page, string text, Func<Task> handOn) => new()
    {
        Text = ViewText.Get(text),
        Order = ToolbarItemOrder.Secondary,
        Command = new Command(async () =>
        {
            if (await page.DisplayAlertAsync(ViewText.Get("HandOnTitle"), ViewText.Get("HandOnWarning"), ViewText.Get("Continue"), ViewText.Get("Cancel")))
            {
                await handOn();
            }
        }),
    };
}
