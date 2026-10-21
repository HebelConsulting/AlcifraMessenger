using SimplArchive.Messenger.Client.Api;
using SimplArchive.Messenger.Client.Display;

namespace SimplArchive.Messenger.Client.Tests;

/// <summary>
/// Cancelling the key dialog showed the thread again, which read it and asked again, endlessly (owner, 2026-10-21): a
/// cancelled prompt is a decision.
/// </summary>
public sealed class KeyPromptTests
{
    [Fact]
    public void A_thread_asks_while_nobody_has_cancelled()
    {
        var prompt = new KeyPrompt();

        foreach (var reason in new[] { ReadReason.Shown, ReadReason.Arrival, ReadReason.Asked })
        {
            Assert.Equal(KeyAsk.Ask, prompt.Begin(reason));
            Assert.False(prompt.End(cancelled: false));
        }
    }

    [Fact]
    public void The_dialog_closing_shows_the_thread_again_before_the_cancel_arrives_and_that_read_is_not_made()
    {
        var prompt = new KeyPrompt();

        Assert.Equal(KeyAsk.Ask, prompt.Begin(ReadReason.Shown));    // opened: the dialog is up
        Assert.Equal(KeyAsk.Skip, prompt.Begin(ReadReason.Shown));   // it closes: the thread is shown again, meanwhile
        Assert.False(prompt.End(cancelled: true));                    // the first read hears of the cancel: no read again

        Assert.Equal(KeyAsk.DoNotAsk, prompt.Begin(ReadReason.Shown));
    }

    [Fact]
    public void Once_cancelled_neither_coming_back_nor_an_arrival_asks_again_but_the_callers_own_ask_does_once()
    {
        var prompt = new KeyPrompt();
        prompt.Begin(ReadReason.Shown);
        prompt.End(cancelled: true);

        Assert.Equal(KeyAsk.DoNotAsk, prompt.Begin(ReadReason.Shown));     // no loop
        Assert.Equal(KeyAsk.DoNotAsk, prompt.Begin(ReadReason.Arrival));   // a push does not raise the dialog either
        Assert.Equal(KeyAsk.Ask, prompt.Begin(ReadReason.Asked));          // a tap, Refresh or a pull asks, once
        Assert.Equal(KeyAsk.Skip, prompt.Begin(ReadReason.Asked));         // a second tap while it is up: not a second dialog
        prompt.End(cancelled: true);                                       // cancelled again
        Assert.Equal(KeyAsk.DoNotAsk, prompt.Begin(ReadReason.Shown));
    }

    [Fact]
    public void Something_that_arrives_while_the_key_is_asked_for_is_read_once_the_key_is_given()
    {
        var prompt = new KeyPrompt();

        Assert.Equal(KeyAsk.Ask, prompt.Begin(ReadReason.Shown));
        Assert.Equal(KeyAsk.Skip, prompt.Begin(ReadReason.Arrival));
        Assert.True(prompt.End(cancelled: false));   // read again: the arrival is not lost
        Assert.Equal(KeyAsk.Ask, prompt.Begin(ReadReason.Shown));
        Assert.False(prompt.End(cancelled: false));
    }

    [Fact]
    public async Task Without_asking_a_message_whose_key_is_not_held_comes_back_sealed_and_the_key_is_not_asked_for()
    {
        var opener = new CountingOpener();
        var session = new ChatSession(new MessengerApi(new HttpClient()), opener, Guid.NewGuid(), "me@example.test");
        var conversation = new Conversation { Id = Guid.NewGuid(), MyKeys = [new EpochKey { Epoch = 1, Key = [1, 2, 3] }] };
        var message = new Message { Id = Guid.NewGuid(), Epoch = 1, Ciphertext = [1] };

        var opened = await session.OpenAsync(conversation, message, askForKey: false);

        Assert.Equal(MessageState.Locked, opened.State);
        Assert.Equal(0, opener.Asked);
        Assert.False(session.Keys.Holds(conversation.Id, 1));

        var row = Assert.Single(ThreadRows.Build(conversation, [opened], Guid.NewGuid()));
        Assert.True(row.IsLocked);
        Assert.True(row.IsPlaceholder);
        Assert.Equal(ViewText.Get("Locked"), row.Text);
        Assert.False(row.CanReply || row.CanEdit);
    }

    [Fact]
    public async Task A_key_held_already_opens_without_asking()
    {
        var opener = new CountingOpener();
        var ring = new KeyRing(opener);
        var conversation = Guid.NewGuid();

        await ring.KeyAsync(conversation, 1, [9]);

        Assert.True(ring.Holds(conversation, 1));
        Assert.False(ring.Holds(conversation, 2));
        Assert.Equal(1, opener.Asked);
    }

    private sealed class CountingOpener : IConversationKeyOpener
    {
        public int Asked { get; private set; }

        public Task<byte[]> OpenAsync(byte[] wrappedKey, CancellationToken cancellationToken = default)
        {
            Asked++;
            return Task.FromResult(new byte[32]);
        }
    }
}
