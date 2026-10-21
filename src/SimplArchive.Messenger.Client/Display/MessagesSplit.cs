using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Client.Display;

/// <summary>
/// When the Messages screen shows the conversations beside the open conversation, and which conversation the right pane
/// keeps once the list was read again — free of any user interface, so a host's tablet layout and its tests agree. The
/// same rule as the SimplArchive app's other wide-window lists: the window's width decides, never the kind of device,
/// and the divider is fixed (the list about a third, the conversation the rest).
/// </summary>
public static class MessagesSplit
{
    /// <summary>The narrowest window, in points, that shows the conversation beside the list.</summary>
    public const double MinimumWidth = 700;

    /// <summary>The list's narrowest column, in points.</summary>
    public const double ListMinimum = 320;

    /// <summary>The list's widest column, in points.</summary>
    public const double ListMaximum = 400;

    /// <summary>Whether a window <paramref name="width"/> points wide shows the pane. A width not yet laid out (reported
    /// as -1) does not.</summary>
    public static bool Splits(double width) => width >= MinimumWidth;

    /// <summary>The list's column in a window <paramref name="width"/> points wide; the conversation takes the rest.</summary>
    public static double ListWidth(double width) => Math.Clamp(width / 3, ListMinimum, ListMaximum);

    /// <summary>
    /// The conversation in <paramref name="rows"/> that is <paramref name="selected"/> — its FRESH row, whose unread
    /// count and links are today's — or null when it left the list (the caller left it, or was removed). Never the row
    /// from before: a pane that keeps showing a conversation the caller is no longer in describes the wrong thing.
    /// </summary>
    public static Conversation? AfterReload(Guid? selected, IReadOnlyList<Conversation> rows) =>
        selected is { } id ? rows.FirstOrDefault(row => row.Id == id) : null;
}
