using System.Globalization;

namespace SimplArchive.Messenger.Client.Display;

/// <summary>A file waiting in the composer as a chip: what it shows (icon, name, size) and the file itself.</summary>
public sealed record DraftFile(Guid Id, OutgoingFile File)
{
    /// <summary>The chip's line: an icon by the kind of file, its name and its size.</summary>
    public string Chip => $"{Icon(File.ContentType)} {File.Name} · {Size(File.Content.LongLength)}";

    /// <summary>A size as a person reads it: 512 B, 3.4 KB, 12 MB.</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{(bytes / 1024.0).ToString("0.#", CultureInfo.CurrentCulture)} KB",
        _ => $"{(bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.CurrentCulture)} MB",
    };

    private static string Icon(string contentType) => contentType switch
    {
        _ when contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => "🖼",
        _ when contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) => "🎞",
        _ when contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => "🎵",
        _ => "📎",
    };
}

/// <summary>What the composer's Send sends.</summary>
public enum DraftSend
{
    /// <summary>Nothing: no text and no file.</summary>
    Nothing,

    /// <summary>A text message.</summary>
    Text,

    /// <summary>One message carrying the files, with the text (if any) as its caption.</summary>
    Files,
}

/// <summary>
/// The composer's draft (owner decision 2026-10-21), free of any user interface: files added as chips above the text
/// field, each removable, several allowed, the text optional — and Send sends ONE message carrying the files with the text
/// as their caption. Sent, the draft empties; a send that failed leaves it as it was, to be sent again.
/// </summary>
public sealed class ComposerDraft
{
    /// <summary>The most files one message carries: the server's limit.</summary>
    public const int MaxFiles = 10;

    /// <summary>The largest file the phone sends: the server takes 100 MB, and sealing adds a little.</summary>
    public const long MaxFileBytes = 95L * 1024 * 1024;

    private readonly List<DraftFile> _files = [];

    /// <summary>The chips, in the order they were added.</summary>
    public IReadOnlyList<DraftFile> Files => _files;

    /// <summary>The draft changed (a chip added or removed, or the draft emptied).</summary>
    public event Action? Changed;

    /// <summary>Adds a file as a chip; null when it was, else the text explaining why not (too large, too many).</summary>
    public string? Add(OutgoingFile file)
    {
        if (file.Content.LongLength > MaxFileBytes)
        {
            return ViewText.Get("FileTooLarge");
        }

        if (_files.Count >= MaxFiles)
        {
            return ViewText.Get("TooManyFiles", MaxFiles);
        }

        _files.Add(new DraftFile(Guid.NewGuid(), file));
        Changed?.Invoke();
        return null;
    }

    /// <summary>Removes the chip <paramref name="id"/>.</summary>
    public void Remove(Guid id)
    {
        if (_files.RemoveAll(f => f.Id == id) > 0)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>What Send sends with <paramref name="text"/> in the field.</summary>
    public DraftSend What(string? text) =>
        _files.Count > 0 ? DraftSend.Files : string.IsNullOrWhiteSpace(text) ? DraftSend.Nothing : DraftSend.Text;

    /// <summary>The files to send, in order.</summary>
    public IReadOnlyList<OutgoingFile> Outgoing => [.. _files.Select(f => f.File)];

    /// <summary>Sent: the chips go.</summary>
    public void Clear()
    {
        if (_files.Count > 0)
        {
            _files.Clear();
            Changed?.Invoke();
        }
    }
}
