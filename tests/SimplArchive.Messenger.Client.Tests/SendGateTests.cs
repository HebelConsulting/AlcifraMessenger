using SimplArchive.Messenger.Client.Display;

namespace SimplArchive.Messenger.Client.Tests;

/// <summary>A slow server once received one message seven times, one for each tap of Send (#26 follow-up).</summary>
public sealed class SendGateTests
{
    [Fact]
    public async Task Two_taps_while_the_first_is_on_its_way_send_once()
    {
        var gate = new SendGate();
        var sends = 0;
        var answer = new TaskCompletionSource();

        var first = gate.SendAsync(() => { sends++; return answer.Task; });
        Assert.True(gate.Sending);
        Assert.False(await gate.SendAsync(() => { sends++; return Task.CompletedTask; }));   // the second tap
        Assert.False(await gate.SendAsync(() => { sends++; return Task.CompletedTask; }));   // and the keyboard's Send

        answer.SetResult();
        Assert.True(await first);
        Assert.Equal(1, sends);
        Assert.False(gate.Sending);
    }

    [Fact]
    public async Task Once_answered_the_next_message_goes()
    {
        var gate = new SendGate();
        var sends = 0;

        Assert.True(await gate.SendAsync(() => { sends++; return Task.CompletedTask; }));
        Assert.True(await gate.SendAsync(() => { sends++; return Task.CompletedTask; }));
        Assert.Equal(2, sends);
    }

    [Fact]
    public async Task A_failed_send_enables_Send_again_and_is_not_repeated()
    {
        var gate = new SendGate();
        var sends = 0;
        var changes = new List<bool>();
        gate.Changed += () => changes.Add(gate.Sending);

        await Assert.ThrowsAsync<HttpRequestException>(() => gate.SendAsync(() => { sends++; throw new HttpRequestException("slow"); }));

        Assert.Equal(1, sends);
        Assert.False(gate.Sending);
        Assert.Equal([true, false], changes);   // disabled while it was on its way, enabled when it answered
    }

    [Fact]
    public void A_message_on_its_way_is_the_callers_offers_nothing_and_says_so()
    {
        var me = Guid.NewGuid();
        var row = ThreadRows.Sending("Hello", me);

        Assert.True(row.IsSending);
        Assert.True(row.IsMine);
        Assert.Equal("Hello", row.Text);
        Assert.Equal(ViewText.Get("Sending"), row.Footer);
        Assert.False(row.CanReply || row.CanEdit || row.CanDeleteForEveryone);
    }
}
