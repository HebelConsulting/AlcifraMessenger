using System.Text.Json;
using SimplArchive.Messenger.Client.Api;

namespace SimplArchive.Messenger.Client.Display;

/// <summary>
/// The Messenger's events off the host's live connection (#26): the host already holds one for its notifications, and
/// hands the screens this view of it, so no second connection is opened. Only the Messenger's own events come through.
/// </summary>
public interface IMessengerLiveEvents
{
    /// <summary>
    /// Calls <paramref name="handler"/> with the name and payload of every event the Messenger module sends, until the
    /// result is disposed — on whatever thread the connection raises it. Nothing is replayed: an event that arrives while
    /// nobody listens is gone, which is what an ephemeral event is.
    /// </summary>
    IDisposable Subscribe(Action<string, string> handler);
}

/// <summary>A host without a live connection (a test, an offline session): no event ever arrives.</summary>
public sealed class NoLiveEvents : IMessengerLiveEvents
{
    /// <summary>The one instance.</summary>
    public static readonly NoLiveEvents Instance = new();

    private NoLiveEvents()
    {
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Action<string, string> handler) => new Nothing();

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

/// <summary>
/// "Is typing" as the module sends it (#26): who started or stopped typing in which conversation. Who types is all it
/// says — never what.
/// </summary>
public sealed record TypingEvent(Guid Conversation, Guid User, bool Typing)
{
    /// <summary>The module's event name.</summary>
    public const string Name = "typing";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The event in <paramref name="payload"/> when <paramref name="name"/> is a typing event and it reads; else
    /// null — a newer module's other events, or a malformed one, are ignored.</summary>
    public static TypingEvent? Parse(string name, string payload)
    {
        if (name != Name)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TypingEvent>(payload, Json) is { Conversation: var c, User: var u } e && c != Guid.Empty && u != Guid.Empty
                ? e : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Whether a conversation's thread sends and shows "is typing" (#26). The indicator is RECIPROCAL (owner decision
/// 2026-10-21): the server offers <c>typing</c> only while the caller's indicator is on and the conversation is live, so
/// one rule answers both — off, the caller's typing reaches nobody and nobody's reaches the caller.
/// </summary>
public static class TypingRule
{
    /// <summary>Whether the caller's typing is sent.</summary>
    public static bool Sends(Conversation conversation) => conversation.Offers("typing") && !conversation.IsReadOnly;

    /// <summary>Whether the others' typing is shown: exactly when the caller's is sent.</summary>
    public static bool Shows(Conversation conversation) => Sends(conversation);
}

/// <summary>What the compose field's <see cref="TypingSignal"/> wants sent.</summary>
public enum TypingSend
{
    /// <summary>Nothing: the members' apps already show what is true.</summary>
    Nothing,

    /// <summary>"I am typing": the first keystroke, or a refresh while typing goes on.</summary>
    Start,

    /// <summary>"I stopped": sent, the field left or emptied, or idle.</summary>
    Stop,
}

/// <summary>
/// The sending side of "is typing" (#26), free of any user interface and of the clock: a start when typing begins, again
/// every <see cref="Refresh"/> while it goes on (each recipient forgets a typer <see cref="TypingIndicator.Expiry"/> after
/// the last), and a stop when the message is sent, the field is left or emptied, or nothing was typed for
/// <see cref="Idle"/>. Whoever drives it passes the time and sends what it answers.
/// </summary>
public sealed class TypingSignal
{
    /// <summary>How often a start is sent again while typing goes on: within the server's 3 s brake on starts, and well
    /// within a recipient's expiry.</summary>
    public static readonly TimeSpan Refresh = TimeSpan.FromSeconds(4);

    /// <summary>How long without a keystroke counts as having stopped.</summary>
    public static readonly TimeSpan Idle = TimeSpan.FromSeconds(5);

    private DateTimeOffset _lastStart;
    private DateTimeOffset _lastKey;

    /// <summary>Whether the members were told the caller types, and not told since that they stopped.</summary>
    public bool IsTyping { get; private set; }

    /// <summary>The field's text changed at <paramref name="now"/>; <paramref name="hasText"/> says whether it holds any.</summary>
    public TypingSend Typed(DateTimeOffset now, bool hasText)
    {
        if (!hasText)
        {
            return Stopped();   // emptied: nothing is being written any more
        }

        _lastKey = now;
        if (IsTyping && now - _lastStart < Refresh)
        {
            return TypingSend.Nothing;
        }

        (IsTyping, _lastStart) = (true, now);
        return TypingSend.Start;
    }

    /// <summary>Time passed: a stop once nothing was typed for <see cref="Idle"/>.</summary>
    public TypingSend Tick(DateTimeOffset now) =>
        IsTyping && now - _lastKey >= Idle ? Stopped() : TypingSend.Nothing;

    /// <summary>The message was sent, or the field left: a stop, if the members were told the caller types.</summary>
    public TypingSend Stopped()
    {
        if (!IsTyping)
        {
            return TypingSend.Nothing;
        }

        IsTyping = false;
        return TypingSend.Stop;
    }
}

/// <summary>
/// The receiving side of "is typing" (#26), free of any user interface and of the clock: who types in one conversation,
/// each forgotten <see cref="Expiry"/> after their last event — so a typer whose stop never arrived (their app closed,
/// the connection dropped) does not hang there — and the line the thread shows under its messages.
/// </summary>
public sealed class TypingIndicator(Guid me)
{
    /// <summary>How long a typer is shown after their last event.</summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromSeconds(6);

    // In the order they began typing, so the line does not shuffle names while they type.
    private readonly List<(Guid User, DateTimeOffset Last)> _typers = [];

    /// <summary>An event arrived at <paramref name="now"/>; true when who types changed.</summary>
    public bool Received(Guid user, bool typing, DateTimeOffset now)
    {
        if (user == me)
        {
            return false;   // the caller's own, from another of their devices: not news to them
        }

        var at = _typers.FindIndex(t => t.User == user);
        if (!typing)
        {
            if (at < 0)
            {
                return false;
            }

            _typers.RemoveAt(at);
            return true;
        }

        if (at >= 0)
        {
            _typers[at] = (user, now);
            return false;
        }

        _typers.Add((user, now));
        return true;
    }

    /// <summary>Forgets the typers whose last event is <see cref="Expiry"/> old at <paramref name="now"/>; true when any was.</summary>
    public bool Expire(DateTimeOffset now) => _typers.RemoveAll(t => now - t.Last >= Expiry) > 0;

    /// <summary>Forgets everyone: the thread left the screen, and what it learns next starts afresh.</summary>
    public void Clear() => _typers.Clear();

    /// <summary>Who types, in the order they began.</summary>
    public IReadOnlyList<Guid> Typers => [.. _typers.Select(t => t.User)];

    /// <summary>The line for <see cref="Typers"/>, named from <paramref name="conversation"/>'s members; null for nobody.</summary>
    public string? Line(Conversation conversation)
    {
        var names = conversation.Members.ToDictionary(m => m.UserId, m => m.DisplayName);
        return Wording([.. Typers.Select(u => names.GetValueOrDefault(u) is { Length: > 0 } name ? name : ViewText.Get("Someone"))]);
    }

    /// <summary>"Anna is typing…", "Anna and Tom are typing…", "Several people are typing…"; null for nobody.</summary>
    public static string? Wording(IReadOnlyList<string> names) => names.Count switch
    {
        0 => null,
        1 => ViewText.Get("TypingOne", names[0]),
        2 => ViewText.Get("TypingTwo", names[0], names[1]),
        _ => ViewText.Get("TypingMany"),
    };
}
