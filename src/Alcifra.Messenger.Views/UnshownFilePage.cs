using Alcifra.Messenger.Client.Display;

namespace Alcifra.Messenger.Views;

/// <summary>
/// A file the app cannot show (#27): its name and what can be done with it. Where files may leave the app, the menu's Open
/// with… hands it on after a warning (<see cref="FileMenu"/>); where they stay in the app, the page says that too. Either
/// way a tap never hands it on.
/// </summary>
internal sealed class UnshownFilePage : ContentPage
{
    public UnshownFilePage(FileOpening opening, string fileName)
    {
        Title = fileName;
        Content = new VerticalStackLayout
        {
            Padding = new Thickness(24, 32),
            Spacing = 16,
            Children =
            {
                new Label { Text = "📎", FontSize = 48, HorizontalTextAlignment = TextAlignment.Center },
                new Label
                {
                    Text = fileName, FontSize = 18, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.MiddleTruncation,
                },
                new Label
                {
                    Text = ViewText.Get(opening.MayLeaveTheApp ? "CannotShow" : "KeptInApp"), FontSize = 15, TextColor = Colors.Gray,
                    HorizontalTextAlignment = TextAlignment.Center,
                },
            },
        };
    }
}
