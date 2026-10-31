using System.Collections.Concurrent;

namespace Alcifra.Messenger;

/// <summary>
/// The server's own brake on "is typing" (#26): one START per <see cref="Interval"/> for a sender in a conversation, so a
/// client that sends on every keystroke still costs the members' apps one event per interval. A STOP always goes, and
/// lets the next start through at once — someone who sent a message and goes on typing is shown again without waiting.
/// </summary>
/// <remarks>
/// Per process: with two app instances a sender may get a start through on each within an interval. The core's live-event
/// rate (20 per sender per 10 s, kept for the whole installation) is the hard limit behind it; this only saves the members'
/// apps the redundant ones. Free of the request, so it is tested on its own clock.
/// </remarks>
public sealed class TypingThrottle(TimeProvider clock)
{
    /// <summary>The shortest gap between two starts of one sender in one conversation.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    // Beyond this many senders, entries older than the interval are dropped: they would let the next start through anyway.
    private const int PruneAbove = 4096;

    private readonly ConcurrentDictionary<(Guid Tenant, Guid Sender, Guid Conversation), DateTimeOffset> _lastStart = new();

    public TypingThrottle()
        : this(TimeProvider.System)
    {
    }

    /// <summary>Whether this start goes out; when it does, the next one waits <see cref="Interval"/>.</summary>
    public bool LetsStartThrough(Guid tenant, Guid sender, Guid conversation)
    {
        var now = clock.GetUtcNow();
        var key = (tenant, sender, conversation);
        var through = false;
        _lastStart.AddOrUpdate(key,
            _ =>
            {
                through = true;
                return now;
            },
            (_, last) =>
            {
                through = now - last >= Interval;
                return through ? now : last;
            });
        Prune(now);
        return through;
    }

    /// <summary>The sender stopped: the stop goes out, and the next start is not held back.</summary>
    public void Stopped(Guid tenant, Guid sender, Guid conversation) => _lastStart.TryRemove((tenant, sender, conversation), out _);

    private void Prune(DateTimeOffset now)
    {
        if (_lastStart.Count <= PruneAbove)
        {
            return;
        }

        foreach (var (key, last) in _lastStart)
        {
            if (now - last >= Interval)
            {
                _lastStart.TryRemove(key, out _);
            }
        }
    }
}
