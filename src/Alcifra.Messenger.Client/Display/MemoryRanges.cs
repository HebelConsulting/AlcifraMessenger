namespace Alcifra.Messenger.Client.Display;

/// <summary>A stretch of content held in memory: where it starts and how many bytes it has.</summary>
public readonly record struct ByteRange(long Offset, int Length);

/// <summary>
/// The arithmetic of a player reading bytes held in memory (#27): what a system player's data request asks for, clamped to
/// the content and cut into answers of bounded size, so a player that asks "everything from here to the end" of a large
/// video is answered piece by piece rather than with one copy of the whole file.
/// </summary>
public static class MemoryRanges
{
    /// <summary>The most one answer carries: 1 MiB.</summary>
    public const int ChunkBytes = 1 << 20;

    /// <summary>
    /// The answers to one data request on content of <paramref name="length"/> bytes, in order. The request starts at
    /// <paramref name="requestedOffset"/> for <paramref name="requestedLength"/> bytes, or runs to the end where
    /// <paramref name="toEnd"/> (its length is then to be disregarded: a player may put anything there); it has already
    /// been answered up to <paramref name="currentOffset"/>, where an answer resumes. Whatever lies outside the content is
    /// not answered, so a request past the end gets nothing and an overlong one stops at the end.
    /// </summary>
    public static IEnumerable<ByteRange> Answer(
        long length, long requestedOffset, long requestedLength, long currentOffset, bool toEnd, int chunkBytes = ChunkBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkBytes);
        var start = Math.Clamp(Math.Max(requestedOffset, currentOffset), 0, length);
        var from = Math.Clamp(requestedOffset, 0, length);
        // Compared, not added: a request "to the end" may carry a length near long.MaxValue, which an addition overflows.
        var end = toEnd || requestedLength >= length - from ? length : from + Math.Max(requestedLength, 0);
        for (var at = start; at < end; at += chunkBytes)
        {
            yield return new ByteRange(at, (int)Math.Min(chunkBytes, end - at));
        }
    }
}
