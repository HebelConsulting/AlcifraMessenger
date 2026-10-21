using SimplArchive.Messenger.Client.Display;

namespace SimplArchive.Messenger.Client.Tests;

// A video or a sound plays from memory (#27): the system player's data requests are answered from the bytes, clamped to
// them and in bounded pieces; and one rule says what plays, for the Messenger's player and a host's preview alike.
public sealed class MemoryPlaybackTests
{
    [Fact]
    public void A_request_is_answered_from_its_offset_for_its_length()
    {
        Assert.Equal([new ByteRange(100, 50)], MemoryRanges.Answer(1000, requestedOffset: 100, requestedLength: 50, currentOffset: 100, toEnd: false));
    }

    [Fact]
    public void The_first_request_for_two_bytes_gets_two_bytes()
    {
        // The player's opening request asks the content's type and length, with the first two bytes.
        Assert.Equal([new ByteRange(0, 2)], MemoryRanges.Answer(1000, 0, 2, 0, toEnd: false));
    }

    [Fact]
    public void A_request_to_the_end_disregards_its_length()
    {
        Assert.Equal([new ByteRange(900, 100)], MemoryRanges.Answer(1000, 900, requestedLength: 7, currentOffset: 900, toEnd: true));
        Assert.Equal([new ByteRange(900, 100)], MemoryRanges.Answer(1000, 900, long.MaxValue, 900, toEnd: true));
    }

    [Fact]
    public void An_overlong_request_stops_at_the_end_without_overflowing()
    {
        Assert.Equal([new ByteRange(990, 10)], MemoryRanges.Answer(1000, 990, long.MaxValue, 990, toEnd: false));
        Assert.Equal([new ByteRange(990, 10)], MemoryRanges.Answer(1000, 990, 500, 990, toEnd: false));
    }

    [Fact]
    public void A_request_past_the_end_or_of_nothing_gets_nothing()
    {
        Assert.Empty(MemoryRanges.Answer(1000, 1000, 10, 1000, toEnd: false));
        Assert.Empty(MemoryRanges.Answer(1000, 5000, 10, 5000, toEnd: true));
        Assert.Empty(MemoryRanges.Answer(1000, 10, 0, 10, toEnd: false));
        Assert.Empty(MemoryRanges.Answer(0, 0, 2, 0, toEnd: false));
    }

    [Fact]
    public void A_resumed_request_continues_where_it_was_answered_up_to()
    {
        Assert.Equal([new ByteRange(150, 50)], MemoryRanges.Answer(1000, 100, 100, currentOffset: 150, toEnd: false));
    }

    [Fact]
    public void A_large_request_is_answered_in_pieces_that_cover_it_exactly()
    {
        var answers = MemoryRanges.Answer(10_000_000, 1_000, 0, 1_000, toEnd: true).ToList();

        Assert.All(answers, a => Assert.InRange(a.Length, 1, MemoryRanges.ChunkBytes));
        Assert.Equal(1_000, answers[0].Offset);
        Assert.Equal(10_000_000 - 1_000, answers.Sum(a => (long)a.Length));
        Assert.All(answers.Zip(answers.Skip(1)), p => Assert.Equal(p.First.Offset + p.First.Length, p.Second.Offset));
    }

    [Fact]
    public void Negative_values_are_clamped_to_the_content()
    {
        Assert.Equal([new ByteRange(0, 10)], MemoryRanges.Answer(1000, -5, 10, -5, toEnd: false));
    }

    [Theory]
    [InlineData("video/mp4", "public.mpeg-4")]
    [InlineData("video/quicktime", "com.apple.quicktime-movie")]
    [InlineData("audio/mp4", "com.apple.m4a-audio")]
    [InlineData("audio/mpeg", "public.mp3")]
    [InlineData("audio/wav", "com.microsoft.waveform-audio")]
    public void A_played_type_tells_the_system_player_its_container(string type, string identifier)
    {
        Assert.True(MediaTypes.Plays(type));
        Assert.Equal(identifier, MediaTypes.IdentifierOf(type));
    }

    [Theory]
    [InlineData("video/webm")]
    [InlineData("audio/ogg")]
    [InlineData("application/pdf")]
    public void A_type_one_platform_cannot_play_is_not_played(string type)
    {
        Assert.False(MediaTypes.Plays(type));
        Assert.Null(MediaTypes.IdentifierOf(type));
    }

    [Fact]
    public void Every_played_type_has_an_identifier_and_opens_in_the_player()
    {
        foreach (var type in new[] { "video/mp4", "video/x-m4v", "video/quicktime", "video/3gpp", "audio/mpeg", "audio/mp4", "audio/x-m4a", "audio/aac", "audio/wav", "audio/x-wav", "audio/flac", "audio/3gpp" })
        {
            Assert.NotNull(MediaTypes.IdentifierOf(type));
            Assert.Equal(FileShownIn.Player, FileOpening.Of([0, 0, 0, 0], "file", type, filesStayInApp: false).ShownIn);
        }
    }
}
