using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SimplArchive.Messenger.ReadModel;
using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger.Api;

/// <summary>A conversation's messages, followed from its <c>messages</c> link. Each message carries its own addresses.</summary>
[ApiController]
[Authorize]
[Route(ConversationsController.Base + "/{conversationId:guid}/messages")]
public sealed class MessagesController(Messages messages) : ControllerBase
{
    /// <summary>A page, oldest first; <c>next</c> continues after its last message.</summary>
    [HttpGet]
    public async Task<ActionResult<MessagePageResource>> Page(Guid conversationId, [FromQuery] Guid? after, [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var (view, page) = await messages.PageAsync(conversationId, after, limit, cancellationToken);
        var readOnly = view.IsReadOnly;
        var self = Self(conversationId);
        var files = await messages.FilesOfAsync(page, cancellationToken);
        return new MessagePageResource
        {
            Items = [.. page.Select(m => Resource(conversationId, m, files.GetValueOrDefault(m.Id) ?? [], readOnly))],
            Links =
            [
                new Link("self", self, "GET"),
                .. readOnly ? Array.Empty<Link>() : [new Link("send", self, "POST")],
                .. page.Count > 0 ? [new Link("next", $"{self}?after={page[^1].Id}&limit={limit}", "GET")] : Array.Empty<Link>(),
            ],
        };
    }

    // Standing convention: every GET has its own HEAD.
    [HttpHead]
    public async Task<IActionResult> Head(Guid conversationId, CancellationToken cancellationToken)
    {
        await messages.PageAsync(conversationId, null, 1, cancellationToken);
        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<MessageResource>> Send(Guid conversationId, [FromBody] SendRequest request, CancellationToken cancellationToken)
    {
        var carried = request.Files();
        var message = await messages.SendAsync(conversationId, request.Id, request.Epoch, request.Ciphertext, request.ReplyToId,
            carried, cancellationToken);
        return Created($"{Self(conversationId)}/{message.Id}", Resource(conversationId, message, carried));
    }

    /// <summary>Edit your own message: the whole new ciphertext, with the If-Match of the version you read.</summary>
    [HttpPut("{messageId:guid}")]
    public async Task<ActionResult<MessageResource>> Edit(Guid conversationId, Guid messageId, [FromBody] EditRequest request, CancellationToken cancellationToken)
    {
        var message = await messages.EditAsync(conversationId, messageId, IfMatch(), request.Epoch, request.Ciphertext, cancellationToken);
        Response.Headers.ETag = Tag(message);
        return Resource(conversationId, message, []);   // a message with files is not edited
    }

    /// <summary>Delete your own message for everyone, with the If-Match of the version you read.</summary>
    [HttpDelete("{messageId:guid}")]
    public async Task<IActionResult> Delete(Guid conversationId, Guid messageId, CancellationToken cancellationToken)
    {
        await messages.DeleteForEveryoneAsync(conversationId, messageId, IfMatch(), cancellationToken);
        return NoContent();
    }

    /// <summary>Delete for me: the message is hidden from the caller only.</summary>
    [HttpPut("{messageId:guid}/hidden")]
    public async Task<IActionResult> Hide(Guid conversationId, Guid messageId, CancellationToken cancellationToken)
    {
        await messages.HideForMeAsync(conversationId, messageId, cancellationToken);
        return NoContent();
    }

    private Guid? IfMatch() =>
        Request.Headers.IfMatch.ToString().Trim('"') is { Length: > 0 } raw && Guid.TryParse(raw, out var token) ? token : null;

    private static string Self(Guid conversationId) => $"/{ConversationsController.Base}/{conversationId}/messages";

    private static string Tag(Message message) => $"\"{message.ConcurrencyToken}\"";

    private static MessageResource Resource(Guid conversationId, Message m, IReadOnlyList<Guid> files, bool readOnly = false)
    {
        var self = $"{Self(conversationId)}/{m.Id}";
        return new MessageResource
        {
            Id = m.Id,
            Epoch = m.Epoch,
            SenderUserId = m.SenderUserId,
            SentAt = m.SentAt,
            EditedAt = m.EditedAt,
            DeletedAt = m.DeletedAt,
            ReplyToId = m.ReplyToId,
            AttachmentDocumentId = m.AttachmentDocumentId,
            Ciphertext = m.Ciphertext,
            ETag = Tag(m),
            Files = [.. files.Select(file => new MessageFileResource
            {
                DocumentId = file,
                Links = [new Link("content", $"/{ConversationsController.Base}/{conversationId}/files/{file}", "GET")],
            })],
            Restored = m.RestoredByUserId is not null,
            Links =
            [
                // Restored history is read, never changed for everyone; hiding it for oneself still works.
                .. readOnly ? Array.Empty<Link>() : [new Link("edit", self, "PUT"), new Link("delete", self, "DELETE")],
                new Link("hide", $"{self}/hidden", "PUT"),
                .. m.AttachmentDocumentId is { } file
                    ? [new Link("attachment", $"/{ConversationsController.Base}/{conversationId}/files/{file}", "GET")]
                    : Array.Empty<Link>(),
            ],
        };
    }
}
