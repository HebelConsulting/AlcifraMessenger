using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger.Api;

/// <summary>
/// The caller's conversations and their members. Reached from the root's <c>messenger:conversations</c> rel; everything
/// else is followed from the resources this returns.
/// </summary>
[ApiController]
[Authorize]
[Route(Base)]
public sealed class ConversationsController(Conversations conversations, Messages messages, Restores restores, IModuleArchiveFacade archive) : ControllerBase
{
    public const string Base = "api/modules/messenger/conversations";

    [HttpGet]
    public async Task<ActionResult<ConversationListResource>> List(CancellationToken cancellationToken) =>
        new ConversationListResource
        {
            Items = [.. (await conversations.MineAsync(cancellationToken)).Select(Resource)],
            Links =
            [
                new Link("self", $"/{Base}", "GET"),
                new Link("open-direct", $"/{Base}/direct", "POST"),
                new Link("create-group", $"/{Base}", "POST"),
                new Link("restore", $"/{Base}/restores", "POST"),
            ],
        };

    // Standing convention: every GET has its own HEAD.
    [HttpHead]
    public async Task<IActionResult> HeadList(CancellationToken cancellationToken)
    {
        await conversations.MineAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ConversationResource>> Get(Guid id, CancellationToken cancellationToken) =>
        Resource(await conversations.MemberViewAsync(id, cancellationToken));

    [HttpHead("{id:guid}")]
    public async Task<IActionResult> Head(Guid id, CancellationToken cancellationToken)
    {
        await conversations.MemberViewAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>The one conversation with a person, created on first use with epoch 1's keys for both.</summary>
    [HttpPost("direct")]
    public async Task<ActionResult<ConversationResource>> OpenDirect([FromBody] OpenDirectRequest request, CancellationToken cancellationToken)
    {
        // The counterpart is taken from the user search, which offers only people with a key, never from the request alone.
        var counterpart = (await archive.SearchUsersAsync(request.CounterpartEmail, 5, cancellationToken))
            .FirstOrDefault(u => u.Id == request.CounterpartUserId)
            ?? throw MessengerErrors.NoKey("This person");
        var id = await conversations.OpenDirectAsync(counterpart, Keys(request.Keys), cancellationToken);
        return Resource(await conversations.MemberViewAsync(id, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<ConversationResource>> CreateGroup([FromBody] CreateGroupRequest request, CancellationToken cancellationToken)
    {
        var id = await conversations.CreateGroupAsync(request.Name, Keys(request.Keys), cancellationToken);
        var view = await conversations.MemberViewAsync(id, cancellationToken);
        return Created($"/{Base}/{id}", Resource(view));
    }

    [HttpPost("{id:guid}/members")]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddMemberRequest request, CancellationToken cancellationToken)
    {
        await conversations.AddMemberAsync(id, request.UserId, Keys(request.Keys), cancellationToken);
        return NoContent();
    }

    /// <summary>Leave (yourself) or remove (an administrator), with the next epoch's keys for whoever remains.</summary>
    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, [FromBody] RemoveMemberRequest request, CancellationToken cancellationToken)
    {
        await conversations.RemoveMemberAsync(id, userId, Keys(request.NextKeys), cancellationToken);
        return NoContent();
    }

    /// <summary>An administrator makes a member an administrator (PUT) or takes it back (DELETE).</summary>
    [HttpPut("{id:guid}/admins/{userId:guid}")]
    public async Task<IActionResult> MakeAdmin(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        await conversations.SetAdminAsync(id, userId, admin: true, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}/admins/{userId:guid}")]
    public async Task<IActionResult> RevokeAdmin(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        await conversations.SetAdminAsync(id, userId, admin: false, cancellationToken);
        return NoContent();
    }

    /// <summary>After somebody left, a remaining member publishes the next epoch's keys.</summary>
    [HttpPost("{id:guid}/epochs")]
    public async Task<IActionResult> PublishEpoch(Guid id, [FromBody] PublishEpochRequest request, CancellationToken cancellationToken)
    {
        await conversations.PublishEpochAsync(id, Keys(request.Keys), cancellationToken);
        return NoContent();
    }

    /// <summary>One conversation of a backup (core #1813): created as read-only history, or joined when another member restored it.</summary>
    [HttpPost("restores")]
    public async Task<ActionResult<ConversationResource>> Restore([FromBody] RestoreRequest request, CancellationToken cancellationToken)
    {
        var id = await restores.BeginAsync(new Restores.Request(
            request.OriginConversationId, request.OriginInstance, request.Name,
            [.. request.Members.Select(m => new Restores.Origin(m.UserId, m.Email, m.DisplayName))],
            request.MyUserId, request.Keys.ToDictionary(k => k.Epoch, k => k.Key)), cancellationToken);
        return Resource(await conversations.MemberViewAsync(id, cancellationToken));
    }

    /// <summary>A batch of a restored conversation's messages, ids kept; those already here are skipped.</summary>
    [HttpPost("{id:guid}/imports")]
    public async Task<ActionResult<ImportResultResource>> Import(Guid id, [FromBody] ImportRequest request, CancellationToken cancellationToken)
    {
        var (imported, skipped) = await restores.ImportAsync(id, [.. request.Messages.Select(m => new Restores.ImportedMessage(
            m.Id, m.Epoch, m.SenderUserId, m.SentAt, m.EditedAt, m.DeletedAt, m.ReplyToId, m.AttachmentDocumentId, m.Ciphertext))], cancellationToken);
        return new ImportResultResource { Imported = imported, Skipped = skipped };
    }

    [HttpPut("{id:guid}/receipt")]
    public async Task<IActionResult> MarkRead(Guid id, [FromBody] ReceiptRequest request, CancellationToken cancellationToken)
    {
        await messages.MarkReadAsync(id, request.ReadUpTo, cancellationToken);
        return NoContent();
    }

    private static List<WrappedKey> Keys(IEnumerable<KeyRequest> keys) => [.. keys.Select(k => new WrappedKey(k.UserId, k.Key))];

    private static ConversationResource Resource(Conversations.View view)
    {
        var self = $"/{Base}/{view.Id}";
        return new ConversationResource
        {
            Id = view.Id,
            IsDirect = view.IsDirect,
            Name = view.Name,
            CounterpartUserId = view.CounterpartUserId,
            Epoch = view.Epoch,
            MyKey = view.MyKey,
            MyKeys = [.. view.MyKeys.OrderBy(k => k.Key).Select(k => new EpochKeyResource { Epoch = k.Key, Key = k.Value })],
            CounterpartName = view.CounterpartUserId is { } counterpart ? view.Names.GetValueOrDefault(counterpart) : null,
            NeedsNewEpoch = view.NeedsNewEpoch,
            // Each member carries the address that removes them; the caller's own is how they leave. A one-to-one chat
            // has neither: it stays between two people.
            Members = [.. view.Members.Select(m => new MemberResource
            {
                UserId = m,
                IsAdmin = view.AdminIds.Contains(m),
                DisplayName = view.Names.GetValueOrDefault(m, string.Empty),
                ReadUpTo = view.ReadUpTo.TryGetValue(m, out var read) ? read : null,
                Email = view.Emails.GetValueOrDefault(m, string.Empty),
                // A restored conversation's members are the backup's, by their ids there: no certificates, nothing to change.
                Links = view.Restored is not null ? [] :
                [
                    // Whose certificates a new epoch's key is sealed to: every member's, on every rekey.
                    new Link("certificates", $"/{PeopleController.Base}/{m}/certificates", "GET"),
                    .. view.IsDirect ? Array.Empty<Link>() : view.IAmAdmin || m == view.Me ? [new Link("remove", $"{self}/members/{m}", "DELETE")] : Array.Empty<Link>(),
                    // A state pair, one at a time: the address is the same, the method says which.
                    .. view.IsDirect || !view.IAmAdmin ? Array.Empty<Link>()
                        : view.AdminIds.Contains(m) ? [new Link("revoke-admin", $"{self}/admins/{m}", "DELETE")]
                        : [new Link("make-admin", $"{self}/admins/{m}", "PUT")],
                ],
            })],
            Admins = [.. view.Admins],
            Unread = view.Unread,
            CanManageMembers = !view.IsDirect && view.IAmAdmin,
            SealedUnderId = view.SealedUnder,
            MyUserId = view.MyOriginUserId ?? view.Me,
            Restored = view.Restored is not null,
            RestoredFrom = view.Restored?.OriginInstance,
            IsReadOnly = view.IsReadOnly,
            Links =
            [
                new Link("self", self, "GET"),
                new Link("messages", $"{self}/messages", "GET"),
                new Link("receipt", $"{self}/receipt", "PUT"),
                // A restore files a backup's files here too, before importing the messages that carry them.
                new Link("upload-file", $"{self}/files", "POST"),
                .. view.Restored is not null ? [new Link("import", $"{self}/imports", "POST")] : Array.Empty<Link>(),
                .. !view.IsDirect && view.IAmAdmin ? [new Link("add-member", $"{self}/members", "POST")] : Array.Empty<Link>(),
                .. view.NeedsNewEpoch ? [new Link("publish-epoch", $"{self}/epochs", "POST")] : Array.Empty<Link>(),
            ],
        };
    }
}
