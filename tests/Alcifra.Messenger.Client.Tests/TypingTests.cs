using System.Globalization;
using Alcifra.Messenger.Client.Api;
using Alcifra.Messenger.Client.Display;

namespace Alcifra.Messenger.Client.Tests;

// "Is typing" (#26) as every app must do it: when the compose field sends a start, a refresh and a stop; how long a
// typer is shown; the line for one, two and more; and the reciprocal rule.
public sealed class TypingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Me = Guid.NewGuid(), Anna = Guid.NewGuid(), Tom = Guid.NewGuid(), Eve = Guid.NewGuid();

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    [Fact]
    public void A_start_when_typing_begins_a_refresh_while_it_goes_on_and_nothing_in_between()
    {
        var signal = new TypingSignal();
        Assert.Equal(TypingSend.Start, signal.Typed(At(0), hasText: true));
        Assert.Equal(TypingSend.Nothing, signal.Typed(At(1), hasText: true));
        Assert.Equal(TypingSend.Nothing, signal.Typed(At(3.9), hasText: true));
        Assert.Equal(TypingSend.Start, signal.Typed(At(4), hasText: true));   // the refresh: recipients keep showing it
        Assert.True(TypingSignal.Refresh < TypingIndicator.Expiry);
        Assert.True(TypingSignal.Refresh >= TimeSpan.FromSeconds(3));          // not held back by the server's brake
    }

    [Fact]
    public void A_stop_when_sent_left_emptied_or_idle_and_only_once()
    {
        var signal = new TypingSignal();
        Assert.Equal(TypingSend.Nothing, signal.Stopped());                    // never started: nothing to take back

        signal.Typed(At(0), true);
        Assert.Equal(TypingSend.Stop, signal.Stopped());                       // sent, or the field left
        Assert.Equal(TypingSend.Nothing, signal.Stopped());

        signal.Typed(At(10), true);
        Assert.Equal(TypingSend.Stop, signal.Typed(At(11), hasText: false));   // emptied

        signal.Typed(At(20), true);
        Assert.Equal(TypingSend.Nothing, signal.Tick(At(24.9)));
        Assert.Equal(TypingSend.Stop, signal.Tick(At(25)));                    // five seconds without a keystroke
        Assert.Equal(TypingSend.Nothing, signal.Tick(At(30)));
        Assert.Equal(TypingSend.Start, signal.Typed(At(31), true));            // typing again starts afresh
    }

    [Fact]
    public void A_typer_is_shown_until_they_stop_or_six_seconds_after_their_last_event()
    {
        var shown = new TypingIndicator(Me);
        Assert.True(shown.Received(Anna, true, At(0)));
        Assert.False(shown.Received(Anna, true, At(4)));    // a refresh: the same line, a later expiry
        Assert.False(shown.Expire(At(9.9)));
        Assert.True(shown.Expire(At(10)));                   // her stop never came: gone six seconds after the refresh
        Assert.Empty(shown.Typers);

        shown.Received(Tom, true, At(20));
        Assert.True(shown.Received(Tom, false, At(21)));
        Assert.Empty(shown.Typers);
        Assert.False(shown.Received(Me, true, At(22)));     // the caller's own, from another of their devices
        Assert.Empty(shown.Typers);
    }

    [Fact]
    public void Typers_are_listed_in_the_order_they_began()
    {
        var shown = new TypingIndicator(Me);
        shown.Received(Tom, true, At(0));
        shown.Received(Anna, true, At(1));
        shown.Received(Tom, true, At(2));
        Assert.Equal([Tom, Anna], shown.Typers);
    }

    [Theory]
    [InlineData("en", "Anna is typing…", "Anna and Tom are typing…", "Several people are typing…")]
    [InlineData("de", "Anna schreibt…", "Anna und Tom schreiben…", "Mehrere Personen schreiben…")]
    public void One_two_and_more_typers_read_as_people_say_it(string language, string one, string two, string many)
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(language);
            Assert.Null(TypingIndicator.Wording([]));
            Assert.Equal(one, TypingIndicator.Wording(["Anna"]));
            Assert.Equal(two, TypingIndicator.Wording(["Anna", "Tom"]));
            Assert.Equal(many, TypingIndicator.Wording(["Anna", "Tom", "Eve"]));
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }

    [Fact]
    public void The_line_names_the_members_and_someone_for_an_account_without_a_name()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en");
            var group = new Conversation
            {
                Members = [new Member { UserId = Me, DisplayName = "Me" }, new Member { UserId = Anna, DisplayName = "Anna" }, new Member { UserId = Eve }],
            };
            var shown = new TypingIndicator(Me);
            Assert.Null(shown.Line(group));
            shown.Received(Anna, true, At(0));
            Assert.Equal("Anna is typing…", shown.Line(group));
            shown.Received(Eve, true, At(0));
            Assert.Equal("Anna and Someone are typing…", shown.Line(group));
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }

    [Fact]
    public void Reciprocal_a_conversation_without_typing_neither_sends_nor_shows()
    {
        var on = new Conversation { Links = [new Link("typing", "/x/typing", "PUT")] };
        var off = new Conversation();
        var history = new Conversation { Links = [new Link("typing", "/x/typing", "PUT")], IsReadOnly = true };

        Assert.True(TypingRule.Sends(on) && TypingRule.Shows(on));
        Assert.False(TypingRule.Sends(off) || TypingRule.Shows(off));
        Assert.False(TypingRule.Sends(history) || TypingRule.Shows(history));
    }

    [Fact]
    public void Only_a_well_formed_typing_event_is_read()
    {
        var conversation = Guid.NewGuid();
        Assert.Equal(new TypingEvent(conversation, Anna, true),
            TypingEvent.Parse("typing", $$"""{"conversation":"{{conversation}}","user":"{{Anna}}","typing":true}"""));
        Assert.Null(TypingEvent.Parse("something-else", "{}"));
        Assert.Null(TypingEvent.Parse("typing", "not json"));
        Assert.Null(TypingEvent.Parse("typing", "{}"));
    }
}
