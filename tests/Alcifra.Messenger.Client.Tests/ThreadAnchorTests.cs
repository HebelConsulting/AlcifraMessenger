using Alcifra.Messenger.Client.Display;

namespace Alcifra.Messenger.Client.Tests;

// The thread's bottom anchor (owner findings 2026-10-21): the newest message on open, after sending and after a reload at
// the end; a reader who scrolled up keeps their place and is offered "New messages ↓"; the keyboard keeps an anchored
// thread at the end.
public sealed class ThreadAnchorTests
{
    private static readonly Guid First = Guid.NewGuid(), Second = Guid.NewGuid(), Third = Guid.NewGuid();

    [Fact]
    public void Opening_sending_and_a_reload_at_the_end_show_the_newest()
    {
        var anchor = new ThreadAnchor();
        Assert.Equal(ThreadScroll.ToEnd, anchor.RowsSet(ThreadRead.Opened, First));
        Assert.Equal(ThreadScroll.ToEnd, anchor.RowsSet(ThreadRead.Reloaded, Second));   // a push while at the end
        Assert.Equal(ThreadScroll.ToEnd, anchor.RowsSet(ThreadRead.Sent, Third));
        Assert.False(anchor.OffersNew);
    }

    [Fact]
    public void A_reader_scrolled_up_keeps_their_place_and_is_offered_what_arrived_below()
    {
        var anchor = new ThreadAnchor();
        anchor.RowsSet(ThreadRead.Opened, First);
        anchor.Scrolled(lastVisible: 3, count: 20, byReader: true);

        Assert.Equal(ThreadScroll.Stay, anchor.RowsSet(ThreadRead.Reloaded, First));            // nothing new: no offer
        Assert.False(anchor.OffersNew);
        Assert.Equal(ThreadScroll.StayAndOfferNew, anchor.RowsSet(ThreadRead.Reloaded, Second));
        Assert.True(anchor.OffersNew);

        Assert.Equal(ThreadScroll.ToEnd, anchor.JumpToEnd());                                   // "New messages ↓" taken
        Assert.False(anchor.OffersNew);
        Assert.True(anchor.AtEnd);
    }

    [Fact]
    public void Scrolling_back_down_to_the_newest_takes_the_offer_away()
    {
        var anchor = new ThreadAnchor();
        anchor.RowsSet(ThreadRead.Opened, First);
        anchor.Scrolled(2, 20, byReader: true);
        anchor.RowsSet(ThreadRead.Reloaded, Second);

        anchor.Scrolled(20, 21, byReader: true);
        Assert.True(anchor.AtEnd);
        Assert.False(anchor.OffersNew);
    }

    [Fact]
    public void The_keyboard_keeps_an_anchored_thread_at_the_end_and_leaves_a_reader_where_they_are()
    {
        var anchor = new ThreadAnchor();
        anchor.RowsSet(ThreadRead.Opened, First);
        anchor.Scrolled(15, 20, byReader: false);   // the keyboard shrank the list: a layout change, not the reader
        Assert.Equal(ThreadScroll.ToEnd, anchor.Resized());

        anchor.Scrolled(4, 20, byReader: true);
        Assert.Equal(ThreadScroll.Stay, anchor.Resized());
    }

    [Fact]
    public void Sending_from_up_in_the_thread_shows_the_message_sent()
    {
        var anchor = new ThreadAnchor();
        anchor.RowsSet(ThreadRead.Opened, First);
        anchor.Scrolled(1, 20, byReader: true);
        Assert.Equal(ThreadScroll.ToEnd, anchor.RowsSet(ThreadRead.Sent, Second));
        Assert.True(anchor.AtEnd);
    }
}
