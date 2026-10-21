using AVFoundation;
using Foundation;
using SimplArchive.Messenger.Client.Display;

namespace SimplArchive.Messenger.Views;

/// <summary>
/// Answers the system player's requests for an asset from the bytes in memory (#27): what it is (its uniform type, its
/// length, that any range may be asked for) and each range it asks, a seek's and a "to the end" alike, cut into bounded
/// answers by <see cref="MemoryRanges"/>. A request the player gives up (it seeked elsewhere) stops being answered.
/// </summary>
internal sealed class MemoryAssetLoader(byte[] content, string identifier) : AVAssetResourceLoaderDelegate
{
    public override bool ShouldWaitForLoadingOfRequestedResource(AVAssetResourceLoader resourceLoader, AVAssetResourceLoadingRequest loadingRequest)
    {
        if (loadingRequest.ContentInformationRequest is { } information)
        {
            information.ContentType = identifier;
            information.ContentLength = content.LongLength;
            information.ByteRangeAccessSupported = true;
        }

        if (loadingRequest.DataRequest is not { } data)
        {
            loadingRequest.FinishLoading();
            return true;
        }

        // Answered off the loader's queue, so a long "to the end" does not hold up the next request (a seek) behind it.
        var (offset, length, current, toEnd) = (data.RequestedOffset, (long)data.RequestedLength, data.CurrentOffset, data.RequestsAllDataToEndOfResource);
        _ = Task.Run(() =>
        {
            foreach (var range in MemoryRanges.Answer(content.LongLength, offset, length, current, toEnd))
            {
                if (loadingRequest.IsCancelled)
                {
                    return;
                }

                data.Respond(NSData.FromArray(content.AsSpan((int)range.Offset, range.Length).ToArray()));
            }

            if (!loadingRequest.IsCancelled)
            {
                loadingRequest.FinishLoading();
            }
        });
        return true;
    }
}
