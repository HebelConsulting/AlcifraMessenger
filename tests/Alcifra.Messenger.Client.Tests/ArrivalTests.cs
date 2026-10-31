using Alcifra.Messenger.Client.Display;

namespace Alcifra.Messenger.Client.Tests;

/// <summary>A push reloads the open screen (#26): which screen, and how often.</summary>
public sealed class ArrivalTests
{
    private static readonly Guid Here = Guid.NewGuid(), Elsewhere = Guid.NewGuid();

    [Fact]
    public void The_list_is_concerned_by_every_arrival()
    {
        Assert.True(Arrivals.Concerns(null, Here));
        Assert.True(Arrivals.Concerns(null, null));
    }

    [Fact]
    public void A_thread_is_concerned_by_its_own_conversation_and_by_an_arrival_nobody_can_place()
    {
        Assert.True(Arrivals.Concerns(Here, Here));
        Assert.True(Arrivals.Concerns(Here, null));
        Assert.False(Arrivals.Concerns(Here, Elsewhere));
    }

    [Fact]
    public void Arrivals_close_together_are_read_once()
    {
        var clock = new Clock();
        var reads = 0;
        var screen = new ReloadOnArrival(null, () => { reads++; return Task.CompletedTask; }, clock.Delay);

        screen.Arrived(Here);
        screen.Arrived(Elsewhere);
        screen.Arrived(null);
        Assert.Equal(0, reads);   // the window is not over yet
        Assert.Equal(1, clock.Waiting);

        clock.Elapse();
        Assert.Equal(1, reads);
        Assert.Equal(0, clock.Waiting);
        Assert.Equal(TimeSpan.FromMilliseconds(500), clock.LastAsked);
    }

    [Fact]
    public void An_arrival_during_a_read_is_read_once_more_after_it()
    {
        var clock = new Clock();
        var reads = 0;
        var running = new TaskCompletionSource();
        var screen = new ReloadOnArrival(null, () => { reads++; return reads == 1 ? running.Task : Task.CompletedTask; }, clock.Delay);

        screen.Arrived(null);
        clock.Elapse();
        Assert.Equal(1, reads);

        screen.Arrived(null);
        screen.Arrived(null);
        Assert.Equal(0, clock.Waiting);   // nothing starts beside the running read

        running.SetResult();
        Assert.Equal(1, clock.Waiting);
        clock.Elapse();
        Assert.Equal(2, reads);
        Assert.Equal(0, clock.Waiting);
    }

    [Fact]
    public void A_thread_does_not_read_for_another_conversation()
    {
        var clock = new Clock();
        var reads = 0;
        var thread = new ReloadOnArrival(Here, () => { reads++; return Task.CompletedTask; }, clock.Delay);

        thread.Arrived(Elsewhere);
        Assert.Equal(0, clock.Waiting);

        thread.Arrived(Here);
        clock.Elapse();
        Assert.Equal(1, reads);
    }

    [Fact]
    public void An_arrival_while_the_app_is_not_in_front_is_read_when_it_is_back_and_not_before()
    {
        var clock = new Clock();
        var reads = 0;
        var screen = new ReloadOnArrival(Here, () => { reads++; return Task.CompletedTask; }, clock.Delay);

        screen.Paused();
        screen.Arrived(Here);
        screen.Arrived(Here);
        Assert.Equal(0, clock.Waiting);

        screen.Resumed();
        clock.Elapse();
        Assert.Equal(1, reads);

        screen.Paused();
        screen.Resumed();   // nothing arrived meanwhile: nothing to read
        Assert.Equal(0, clock.Waiting);
    }

    [Fact]
    public void A_stopped_screen_reads_nothing_more_not_even_what_was_pending()
    {
        var clock = new Clock();
        var reads = 0;
        var screen = new ReloadOnArrival(null, () => { reads++; return Task.CompletedTask; }, clock.Delay);

        screen.Arrived(null);
        screen.Stop();
        clock.Elapse();
        screen.Arrived(null);
        screen.Resumed();
        Assert.Equal(0, reads);
        Assert.Equal(0, clock.Waiting);
    }

    [Fact]
    public void A_failed_read_is_dropped_and_the_next_arrival_reads_again()
    {
        var clock = new Clock();
        var reads = 0;
        var screen = new ReloadOnArrival(null, () => { reads++; throw new HttpRequestException("offline"); }, clock.Delay);

        screen.Arrived(null);
        clock.Elapse();
        screen.Arrived(null);
        clock.Elapse();
        Assert.Equal(2, reads);
    }

    // The window elapses when the test says so; continuations run inline, so each step is done when Elapse returns.
    private sealed class Clock
    {
        private readonly List<TaskCompletionSource> _waits = [];

        public int Waiting => _waits.Count(w => !w.Task.IsCompleted);

        public TimeSpan LastAsked { get; private set; }

        public Task Delay(TimeSpan span, CancellationToken cancellationToken)
        {
            LastAsked = span;
            var wait = new TaskCompletionSource();
            cancellationToken.Register(() => wait.TrySetCanceled(cancellationToken));
            _waits.Add(wait);
            return wait.Task;
        }

        public void Elapse()
        {
            foreach (var wait in _waits.Where(w => !w.Task.IsCompleted).ToList())
            {
                wait.TrySetResult();
            }
        }
    }
}
