namespace SimplArchive.Messenger.Views;

/// <summary>What a host registers once so that <see cref="MemoryPlayer"/> plays.</summary>
public static class PlayerHosting
{
    /// <summary>
    /// Registers iOS's player of <see cref="MemoryPlayer"/>, the system player reading from memory; elsewhere it does
    /// nothing. Android's player is the toolkit's media element, which the host registers itself, chained to
    /// <c>UseMauiApp</c> as the toolkit's analyzer demands:
    /// <c>.UseMauiCommunityToolkitMediaElement(isAndroidForegroundServiceEnabled: false).UseMessengerPlayer()</c>.
    /// </summary>
    public static MauiAppBuilder UseMessengerPlayer(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
#if IOS
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<MemoryPlayerSurface, MemoryPlayerHandler>());
#endif
        return builder;
    }
}
