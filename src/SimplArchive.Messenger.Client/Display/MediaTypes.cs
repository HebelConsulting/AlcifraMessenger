namespace SimplArchive.Messenger.Client.Display;

/// <summary>
/// The video and sound both platforms' players play, from memory (#27): the one rule the Messenger's player and a host's
/// document preview both ask, so a type plays in one exactly where it plays in the other. A WebM, an Ogg or an AVI plays
/// on one platform only, so it is none of them.
/// </summary>
public static class MediaTypes
{
    // Each type with the uniform type identifier the system player is told it is: it is handed bytes, not a file, so it
    // reads the container from what it is told rather than from a name.
    private static readonly Dictionary<string, string> Identifiers = new(StringComparer.Ordinal)
    {
        ["video/mp4"] = "public.mpeg-4",
        ["video/x-m4v"] = "com.apple.m4v-video",
        ["video/quicktime"] = "com.apple.quicktime-movie",
        ["video/3gpp"] = "public.3gpp",
        ["audio/mpeg"] = "public.mp3",
        ["audio/mp4"] = "com.apple.m4a-audio",
        ["audio/x-m4a"] = "com.apple.m4a-audio",
        ["audio/aac"] = "public.aac-audio",
        ["audio/wav"] = "com.microsoft.waveform-audio",
        ["audio/x-wav"] = "com.microsoft.waveform-audio",
        ["audio/flac"] = "org.xiph.flac",
        ["audio/3gpp"] = "public.3gpp",
    };

    /// <summary>Whether <paramref name="contentType"/> (lower case, without parameters) is a video or a sound the app plays.</summary>
    public static bool Plays(string contentType) => Identifiers.ContainsKey(contentType);

    /// <summary>
    /// The uniform type identifier of <paramref name="contentType"/>, which the iOS player is given for bytes it reads from
    /// memory; null for a type the app does not play.
    /// </summary>
    public static string? IdentifierOf(string contentType) => Identifiers.GetValueOrDefault(contentType);
}
