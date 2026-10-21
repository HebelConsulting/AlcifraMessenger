using SimplArchive.Messenger.Client.Display;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// A screen's header, the same on a phone and in a tablet's pane: ‹ Back where the pane has somewhere to go back to (a
/// phone's navigation bar has its own), the title, the screen's actions as words, and ⋮ for the secondary ones, which
/// opens the same menu everywhere. Touch has no hover, so an action is spelled out rather than hidden behind an icon. In
/// the pane it is a row of its own over a hairline; on a phone it is drawn into the page's navigation bar, so the bar's
/// colours, the status bar and the host's own entries (its way home) stay the host's.
/// </summary>
internal sealed class ScreenHeader : ContentView
{
    private readonly Color _ink;
    private readonly Color? _titleInk;
    private readonly Page _dialogs;
    private readonly Grid _row = new() { Padding = new Thickness(4) };

    /// <param name="ink">The colour the header's title and buttons are drawn in: the accent in the pane, the bar's text
    /// colour in a navigation bar.</param>
    /// <param name="dialogs">The page the ⋮ menu opens over.</param>
    /// <param name="inBar">Drawn into a navigation bar: no hairline and no padding of its own.</param>
    /// <param name="titleInk">The title's colour where it differs from the buttons' (a bar whose buttons are tinted);
    /// null: the buttons' in a bar, the theme's in the pane.</param>
    public ScreenHeader(Color ink, Page dialogs, bool inBar, Color? titleInk = null)
    {
        (_ink, _dialogs, InBar, _titleInk) = (ink, dialogs, inBar, titleInk);
        if (inBar)
        {
            _row.Padding = new Thickness(0);
            Content = _row;
        }
        else
        {
            Content = new VerticalStackLayout
            {
                Children = { _row, new BoxView { HeightRequest = 1, Color = Color.FromArgb("#1F000000") } },
            };
        }

        IsVisible = false;
    }

    private bool InBar { get; }

    /// <summary>Draws the header for a screen titled <paramref name="title"/>.</summary>
    /// <param name="title">The screen's title.</param>
    /// <param name="actions">The screen's actions; the secondary ones go behind ⋮.</param>
    /// <param name="back">Where ‹ Back goes; null: no Back.</param>
    public void Show(string title, IReadOnlyList<ScreenAction> actions, Func<Task>? back)
    {
        _row.Clear();
        _row.ColumnDefinitions.Clear();
        void Add(View view, GridLength width)
        {
            _row.ColumnDefinitions.Add(new ColumnDefinition(width));
            _row.Add(view, _row.ColumnDefinitions.Count - 1);
        }

        if (back is not null)
        {
            var button = Button("‹", back, fontSize: 26);
            SemanticProperties.SetDescription(button, ViewText.Get("Back"));
            Add(button, GridLength.Auto);
        }

        Add(new Label
        {
            Text = title,
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(back is null && !InBar ? 12 : 0, 0, 0, 0),
            TextColor = _titleInk ?? (InBar ? _ink : null),
        }, GridLength.Star);

        foreach (var action in actions.Where(a => !a.Secondary))
        {
            Add(Button(action.Text, action.Act), GridLength.Auto);
        }

        var more = actions.Where(a => a.Secondary).ToList();
        if (more.Count > 0)
        {
            var menu = Button("⋮", async () =>
            {
                var chosen = await _dialogs.DisplayActionSheetAsync(null, ViewText.Get("Cancel"), null, [.. more.Select(a => a.Text)]);
                if (more.FirstOrDefault(a => a.Text == chosen) is { } action)
                {
                    await action.Act();
                }
            }, fontSize: 20);
            SemanticProperties.SetDescription(menu, ViewText.Get("More"));
            Add(menu, GridLength.Auto);
        }

        IsVisible = true;
    }

    /// <summary>No header: nothing is shown.</summary>
    public void Hide()
    {
        _row.Clear();
        IsVisible = false;
    }

    private Button Button(string text, Func<Task> act, double fontSize = 15)
    {
        var button = new Button { Text = text, BackgroundColor = Colors.Transparent, TextColor = _ink, FontSize = fontSize, Padding = new Thickness(8, 0) };
        button.Clicked += async (_, _) => await act();
        return button;
    }
}
