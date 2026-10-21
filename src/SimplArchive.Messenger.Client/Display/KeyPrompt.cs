namespace SimplArchive.Messenger.Client.Display;

/// <summary>Why a thread reads itself again: what decides whether it may ask for the key.</summary>
public enum ReadReason
{
    /// <summary>It came to the front: opened, or back after a dialog (the key dialog itself) or another page.</summary>
    Shown,

    /// <summary>Something arrived (a push, #26).</summary>
    Arrival,

    /// <summary>The caller asked: a tap on a sealed message, Refresh, a pull, a message sent.</summary>
    Asked,
}

/// <summary>What a read does about the key.</summary>
public enum KeyAsk
{
    /// <summary>It may ask for a key that is not held; it ends with <see cref="KeyPrompt.End"/>.</summary>
    Ask,

    /// <summary>The prompt was cancelled: what is not held shows sealed, and nothing is asked.</summary>
    DoNotAsk,

    /// <summary>A read is waiting for the key already, and shows the thread when it is done: this one is not made.</summary>
    Skip,
}

/// <summary>
/// Whether a thread may ask for the key (owner, 2026-10-21): a cancelled key prompt is a decision, not a reason to ask
/// again. Cancelling the dialog closes it, which shows the thread again — while the read that asked is still finishing —
/// which read it and asked again, endlessly. So: one read that may ask at a time (a read beside it is not made), and once
/// cancelled, a read the caller did not ask for shows the messages sealed and asks nothing; the caller's own ask (a tap
/// on one, Refresh, a pull) asks once more.
/// </summary>
public sealed class KeyPrompt
{
    private bool _asking;
    private bool _skipped;

    /// <summary>Whether the caller cancelled the prompt, and has not asked since.</summary>
    public bool Declined { get; private set; }

    /// <summary>A read for <paramref name="reason"/> begins: whether it may ask, may not, or is not to be made.</summary>
    public KeyAsk Begin(ReadReason reason)
    {
        if (_asking)
        {
            _skipped = true;
            return KeyAsk.Skip;
        }

        if (reason == ReadReason.Asked)
        {
            Declined = false;
        }

        if (Declined)
        {
            return KeyAsk.DoNotAsk;
        }

        _asking = true;
        return KeyAsk.Ask;
    }

    /// <summary>
    /// The read that began with <see cref="KeyAsk.Ask"/> is done; <paramref name="cancelled"/> when the caller cancelled
    /// the prompt. True when a read was not made meanwhile (something arrived while the key was asked for) and the
    /// prompt was not cancelled: the thread is read once more, so nothing that arrived is missed.
    /// </summary>
    public bool End(bool cancelled)
    {
        var again = _skipped && !cancelled;
        (_asking, _skipped) = (false, false);
        Declined |= cancelled;
        return again;
    }

    /// <summary>The caller cancelled a prompt outside a read (sending a message).</summary>
    public void Decline() => Declined = true;
}
