using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger.Api;

/// <summary>
/// Files of a conversation, followed from its <c>upload-file</c> link and a message's <c>attachment</c> link. Bytes never
/// pass through here: they are uploaded to and downloaded from storage directly, sealed.
/// </summary>
[ApiController]
[Authorize]
[Route(ConversationsController.Base + "/{conversationId:guid}/files")]
public sealed class FilesController(Files files) : ControllerBase
{
    public sealed class UploadResource : HypermediaResource
    {
        public string UploadId { get; set; } = string.Empty;

        /// <summary>Where the client PUTs the sealed file (.p7m). A credential for its lifetime: never logged.</summary>
        public Uri Url { get; set; } = new("about:blank");

        public DateTimeOffset ExpiresAt { get; set; }
        public long MaxBytes { get; set; }
    }

    public sealed class FileResource : HypermediaResource
    {
        public Guid DocumentId { get; set; }
    }

    /// <summary>Begins an upload; <c>complete</c> files it once the bytes are there.</summary>
    [HttpPost]
    public async Task<ActionResult<UploadResource>> Begin(Guid conversationId, CancellationToken cancellationToken)
    {
        var upload = await files.BeginAsync(conversationId, cancellationToken);
        return new UploadResource
        {
            UploadId = upload.UploadId,
            Url = upload.Url,
            ExpiresAt = upload.ExpiresAt,
            MaxBytes = upload.MaxBytes,
            Links = [new Link("complete", $"/{ConversationsController.Base}/{conversationId}/files/uploads/{upload.UploadId}", "POST")],
        };
    }

    /// <summary>Files the uploaded bytes in the conversation; a message then names the document.</summary>
    [HttpPost("uploads/{uploadId}")]
    public async Task<ActionResult<FileResource>> Complete(Guid conversationId, string uploadId, CancellationToken cancellationToken)
    {
        var documentId = await files.CompleteAsync(conversationId, uploadId, cancellationToken);
        var self = $"/{ConversationsController.Base}/{conversationId}/files/{documentId}";
        return Created(self, new FileResource { DocumentId = documentId, Links = [new Link("self", self, "GET")] });
    }

    /// <summary>A redirect to the sealed file, for a member; 409 where the installation will not hand it out.</summary>
    [HttpGet("{documentId:guid}")]
    public async Task<IActionResult> Download(Guid conversationId, Guid documentId, CancellationToken cancellationToken) =>
        await files.DownloadAsync(conversationId, documentId, cancellationToken) is { } url
            ? Redirect(url.ToString())
            : throw MessengerErrors.FileNotServed();

    // Standing convention: every GET has its own HEAD.
    [HttpHead("{documentId:guid}")]
    public async Task<IActionResult> Head(Guid conversationId, Guid documentId, CancellationToken cancellationToken) =>
        await files.DownloadAsync(conversationId, documentId, cancellationToken) is not null ? NoContent() : throw MessengerErrors.FileNotServed();
}
