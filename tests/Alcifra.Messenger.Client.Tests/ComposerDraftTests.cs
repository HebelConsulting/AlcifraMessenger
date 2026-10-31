using System.Globalization;
using Alcifra.Messenger.Client.Display;

namespace Alcifra.Messenger.Client.Tests;

// The composer's chips (owner decision 2026-10-21): files wait above the field, removable, several at once, text
// optional — and Send is ONE message with the files and the text as their caption, sent once.
public sealed class ComposerDraftTests
{
    private static OutgoingFile File(string name, int bytes = 10, string type = "application/pdf") => new(new byte[bytes], name, type);

    [Fact]
    public void Chips_are_added_in_order_and_removed_one_by_one()
    {
        var draft = new ComposerDraft();
        var changes = 0;
        draft.Changed += () => changes++;
        Assert.Null(draft.Add(File("a.pdf")));
        Assert.Null(draft.Add(File("b.pdf")));
        Assert.Null(draft.Add(File("c.pdf")));
        draft.Remove(draft.Files[1].Id);

        Assert.Equal(["a.pdf", "c.pdf"], draft.Outgoing.Select(f => f.Name));
        Assert.Equal(4, changes);
        draft.Remove(Guid.NewGuid());   // not a chip here: nothing changes
        Assert.Equal(4, changes);
    }

    [Fact]
    public void Send_is_one_message_with_the_files_and_the_text_optional()
    {
        var draft = new ComposerDraft();
        Assert.Equal(DraftSend.Nothing, draft.What("  "));
        Assert.Equal(DraftSend.Text, draft.What("Hello"));
        draft.Add(File("plan.pdf"));
        Assert.Equal(DraftSend.Files, draft.What(null));     // a file alone, no caption
        Assert.Equal(DraftSend.Files, draft.What("See"));    // the text is the caption of the same message

        draft.Clear();                                       // sent: the chips go
        Assert.Empty(draft.Files);
        Assert.Equal(DraftSend.Nothing, draft.What(string.Empty));
    }

    [Fact]
    public void Too_large_and_too_many_are_said_rather_than_sent()
    {
        var draft = new ComposerDraft();
        Assert.NotNull(draft.Add(new OutgoingFile(new byte[ComposerDraft.MaxFileBytes + 1], "huge.bin", "application/octet-stream")));
        for (var i = 0; i < ComposerDraft.MaxFiles; i++)
        {
            Assert.Null(draft.Add(File($"{i}.pdf")));
        }

        Assert.NotNull(draft.Add(File("one-more.pdf")));
        Assert.Equal(ComposerDraft.MaxFiles, draft.Files.Count);
    }

    [Fact]
    public async Task One_send_at_a_time_covers_the_whole_message()
    {
        var gate = new SendGate();
        var release = new TaskCompletionSource();
        var sends = 0;
        var first = gate.SendAsync(async () =>
        {
            sends++;
            await release.Task;
        });
        Assert.False(await gate.SendAsync(() =>
        {
            sends++;
            return Task.CompletedTask;
        }));   // a second tap while the files are on their way sends nothing
        release.SetResult();
        Assert.True(await first);
        Assert.Equal(1, sends);
    }

    [Fact]
    public void A_chip_names_the_file_its_kind_and_its_size()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal("🖼 harbour.jpg · 2 KB", new DraftFile(Guid.NewGuid(), File("harbour.jpg", 2048, "image/jpeg")).Chip);
            Assert.Equal("📎 plan.pdf · 512 B", new DraftFile(Guid.NewGuid(), File("plan.pdf", 512)).Chip);
            Assert.Equal("3.5 MB", DraftFile.Size(3_670_016));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
