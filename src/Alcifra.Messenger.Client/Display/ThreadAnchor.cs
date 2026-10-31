namespace Alcifra.Messenger.Client.Display;

/// <summary>Why a thread's rows were just set: what decides where it is scrolled to.</summary>
public enum ThreadRead
{
    /// <summary>The thread opened (or came back to the front): the newest message is shown.</summary>
    Opened,

    /// <summary>The caller sent something: their message is shown.</summary>
    Sent,

    /// <summary>Read again by itself or by the caller (a push, a pull, Refresh): at the end it stays at the end; scrolled up
    /// to read older messages, it stays where the reader is.</summary>
    Reloaded,
}

/// <summary>Where a thread goes after its rows were set or its height changed.</summary>
public enum ThreadScroll
{
    /// <summary>Nothing moves.</summary>
    Stay,

    /// <summary>To the newest message, at the bottom.</summary>
    ToEnd,

    /// <summary>Nothing moves, and "New messages ↓" is offered: something arrived below where the reader is.</summary>
    StayAndOfferNew,
}

/// <summary>
/// The thread's bottom anchor (owner findings 2026-10-21), free of any user interface: the newest message is shown when
/// the thread opens, after the caller sends, and after a reload while the reader is at the end — and when the keyboard
/// appears or the field grows, a thread at the end stays at the end, so the last messages sit right above the keyboard.
/// A reader who scrolled up to older messages keeps their place: a reload that brings something new offers
/// "New messages ↓" instead of jumping.
/// </summary>
public sealed class ThreadAnchor
{
    private Guid? _newest;

    /// <summary>Whether the newest message is in view: where a reload and a smaller window keep the thread.</summary>
    public bool AtEnd { get; private set; } = true;

    /// <summary>Whether "New messages ↓" is offered.</summary>
    public bool OffersNew { get; private set; }

    /// <summary>
    /// The thread was scrolled so that row <paramref name="lastVisible"/> of <paramref name="count"/> is the last in view.
    /// <paramref name="byReader"/>: the reader dragged it; a layout change (the keyboard shrinking the list) does not move
    /// the anchor off the end.
    /// </summary>
    public void Scrolled(int lastVisible, int count, bool byReader)
    {
        if (count == 0 || lastVisible >= count - 1)
        {
            (AtEnd, OffersNew) = (true, false);   // the reader reached the newest: nothing below is new any more
        }
        else if (byReader)
        {
            AtEnd = false;
        }
    }

    /// <summary>The rows were set for <paramref name="why"/>; <paramref name="newest"/> is the last row's message id.</summary>
    public ThreadScroll RowsSet(ThreadRead why, Guid? newest)
    {
        var arrived = newest is not null && _newest is not null && newest != _newest;
        _newest = newest;
        if (why != ThreadRead.Reloaded || AtEnd)
        {
            (AtEnd, OffersNew) = (true, false);
            return ThreadScroll.ToEnd;
        }

        if (!arrived)
        {
            return ThreadScroll.Stay;
        }

        OffersNew = true;
        return ThreadScroll.StayAndOfferNew;
    }

    /// <summary>The thread's height changed (the keyboard came or went, the field grew): at the end it stays at the end.</summary>
    public ThreadScroll Resized() => AtEnd ? ThreadScroll.ToEnd : ThreadScroll.Stay;

    /// <summary>The reader took "New messages ↓": to the end, and the offer goes.</summary>
    public ThreadScroll JumpToEnd()
    {
        (AtEnd, OffersNew) = (true, false);
        return ThreadScroll.ToEnd;
    }
}
