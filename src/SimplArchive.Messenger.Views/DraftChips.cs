using SimplArchive.Messenger.Client.Display;
using Microsoft.Maui.Controls.Shapes;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// The composer's files as chips above the text field (owner decision 2026-10-21): icon, name and size, each with ✕ to
/// remove it. Drawn from the <see cref="ComposerDraft"/> whenever it changes; hidden while the draft holds no file.
/// </summary>
internal sealed class DraftChips : ContentView
{
    private readonly ComposerDraft _draft;
    private readonly HorizontalStackLayout _row = new() { Spacing = 6, Padding = new Thickness(8, 4) };

    public DraftChips(ComposerDraft draft, Color accent)
    {
        _draft = draft;
        Content = new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = _row };
        IsVisible = false;
        draft.Changed += () => Dispatcher.Dispatch(() => Draw(accent));
    }

    private void Draw(Color accent)
    {
        _row.Clear();
        foreach (var file in _draft.Files)
        {
            var remove = new Button { Text = "✕", Padding = 0, WidthRequest = 32, HeightRequest = 32, BackgroundColor = Colors.Transparent, TextColor = Colors.Gray };
            SemanticProperties.SetDescription(remove, ViewText.Get("RemoveFile", file.File.Name));
            var id = file.Id;
            remove.Clicked += (_, _) => _draft.Remove(id);
            var chip = new Grid { ColumnSpacing = 2, ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Auto)] };
            chip.Add(new Label
            {
                Text = file.Chip, FontSize = 13, VerticalTextAlignment = TextAlignment.Center, MaximumWidthRequest = 240,
                LineBreakMode = LineBreakMode.MiddleTruncation,
            }, 0);
            chip.Add(remove, 1);
            _row.Add(new Border
            {
                StrokeThickness = 1, Stroke = accent.WithAlpha(0.4f), BackgroundColor = accent.WithAlpha(0.08f),
                StrokeShape = new RoundRectangle { CornerRadius = 16 }, Padding = new Thickness(10, 0, 0, 0), Content = chip,
            });
        }

        IsVisible = _draft.Files.Count > 0;
    }
}
