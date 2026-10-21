namespace SimplArchive.Messenger.Views;

/// <summary>
/// Pull-down to refresh, once for every screen of the Messenger that shows what the server holds (the conversations,
/// a thread): the screen's own read runs again, and the spinner goes when it is done, whatever its outcome.
/// </summary>
internal static class PullToRefresh
{
    /// <summary>Wraps <paramref name="list"/> (a view that scrolls itself) so a pull runs <paramref name="reload"/>.</summary>
    public static RefreshView Around(ItemsView list, Func<Task> reload)
    {
        var refresh = new RefreshView { Content = list };
        refresh.Command = new Command(async () =>
        {
            try
            {
                await reload();
            }
            finally
            {
                refresh.IsRefreshing = false;
            }
        });
        return refresh;
    }
}
