namespace SimplArchive.Messenger.Client.Display;

/// <summary>Where an opened file is shown (#27).</summary>
public enum FileShownIn
{
    /// <summary>A photo, on the Messenger's own photo page, from memory.</summary>
    Photo,

    /// <summary>A PDF or a plain text, in the host's document preview (pages, find, pinch zoom), from memory.</summary>
    Preview,

    /// <summary>A video or a sound, in the Messenger's own player, from memory.</summary>
    Player,

    /// <summary>The app cannot show it; another app can, handed on deliberately from the page's menu after a warning.</summary>
    OtherApp,

    /// <summary>The app cannot show it and the tenant keeps files in the app: the page says so, and nothing hands it on.</summary>
    Nowhere,
}

/// <summary>
/// What opening a sent file does (#27, owner decisions of 2026-10-21): as much as possible opens IN the app, because a
/// decrypted copy handed to another app is a plaintext copy the app no longer controls. Photos, PDFs, plain text, video and
/// sound open in the app; anything else (an office document, say) can only be handed on — never by a tap, always from the
/// page's menu and after a warning — and not at all where the tenant keeps files in the app.
/// </summary>
/// <param name="ShownIn">Where the file is shown.</param>
/// <param name="ContentType">The type it is shown and handed on as: the bytes' own where they say (a PDF sent as
/// <c>application/octet-stream</c> is a PDF), else the sent type, else the file name's.</param>
/// <param name="MayLeaveTheApp">Whether the page offers Open with and Share at all: false where the tenant keeps files in
/// the app, whatever the file.</param>
public sealed record FileOpening(FileShownIn ShownIn, string ContentType, bool MayLeaveTheApp)
{
    private const string Generic = "application/octet-stream";

    // Photos the platforms' image views draw on Android and iOS alike; TIFF and SVG are not among them.
    private static readonly HashSet<string> Photos = ["image/jpeg", "image/png", "image/gif", "image/webp", "image/bmp", "image/heic", "image/heif"];

    // Plain text only: a format with markup of its own (HTML, say) would show as its source, which is not showing it.
    private static readonly HashSet<string> Texts = ["text/plain", "text/csv", "text/markdown"];

    // The file name's type, for a file sent with none worth the name (a picker often says octet-stream).
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain", [".text"] = "text/plain", [".log"] = "text/plain", [".csv"] = "text/csv", [".md"] = "text/markdown",
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".gif"] = "image/gif", [".webp"] = "image/webp",
        [".bmp"] = "image/bmp", [".heic"] = "image/heic", [".heif"] = "image/heif",
        [".mp4"] = "video/mp4", [".m4v"] = "video/x-m4v", [".mov"] = "video/quicktime", [".3gp"] = "video/3gpp",
        [".mp3"] = "audio/mpeg", [".m4a"] = "audio/mp4", [".aac"] = "audio/aac", [".wav"] = "audio/wav", [".flac"] = "audio/flac",
    };

    // Spellings in use for a type above, read as that type.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["image/jpg"] = "image/jpeg", ["image/pjpeg"] = "image/jpeg", ["image/x-ms-bmp"] = "image/bmp",
        ["application/x-pdf"] = "application/pdf", ["text/x-markdown"] = "text/markdown",
        ["audio/mp3"] = "audio/mpeg", ["audio/m4a"] = "audio/mp4", ["audio/x-flac"] = "audio/flac", ["audio/wave"] = "audio/wav",
        ["audio/vnd.wave"] = "audio/wav",
    };

    /// <summary>
    /// What opening <paramref name="content"/> does. The bytes speak first (a PDF, PNG, JPEG or GIF is recognised by its
    /// first bytes, whatever it was sent as), then the sent type, then the file name; what none of them makes a type the
    /// app shows is what the app cannot show.
    /// </summary>
    /// <param name="content">The opened file.</param>
    /// <param name="fileName">Its name, as sent.</param>
    /// <param name="contentType">Its type, as sent.</param>
    /// <param name="filesStayInApp">The tenant keeps files in the app (<see cref="Api.Conversation.FilesStayInApp"/>).</param>
    public static FileOpening Of(byte[] content, string fileName, string contentType, bool filesStayInApp)
    {
        ArgumentNullException.ThrowIfNull(content);
        var declared = Declared(contentType);
        var named = ByExtension.GetValueOrDefault(Path.GetExtension(fileName ?? string.Empty));
        var type = new[] { Sniffed(content), declared, named }.FirstOrDefault(t => t is not null && Shown(t) is not null)
            ?? (declared is null or Generic or "binary/octet-stream" ? named ?? declared : declared)
            ?? Generic;
        var shownIn = Shown(type) ?? (filesStayInApp ? FileShownIn.Nowhere : FileShownIn.OtherApp);
        return new FileOpening(shownIn, type, !filesStayInApp);
    }

    // Where the app shows a type itself; null where it cannot.
    private static FileShownIn? Shown(string type) =>
        Photos.Contains(type) ? FileShownIn.Photo
        : type == "application/pdf" || Texts.Contains(type) ? FileShownIn.Preview
        : MediaTypes.Plays(type) ? FileShownIn.Player
        : null;

    /// <summary>Whether the app shows it itself, as a photo, in the preview or in the player.</summary>
    public bool InTheApp => ShownIn is FileShownIn.Photo or FileShownIn.Preview or FileShownIn.Player;

    // The type the first bytes prove, where they prove one.
    private static string? Sniffed(byte[] b) => b switch
    {
        [0x25, 0x50, 0x44, 0x46, ..] => "application/pdf",                    // %PDF
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x47, 0x49, 0x46, 0x38, ..] => "image/gif",                          // GIF8
        _ => null,
    };

    // The sent type, lower case, without parameters ("text/plain; charset=utf-8") and in its usual spelling; null for none.
    private static string? Declared(string? contentType)
    {
        var type = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        return type.Length == 0 ? null : Aliases.GetValueOrDefault(type, type);
    }
}
