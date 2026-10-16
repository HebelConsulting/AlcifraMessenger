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
public sealed class ConversationsController(Conversations conversations, Messages messages, IModuleArchiveFacade archive) : ControllerBase
{
    public const string Base = "api/modules/messenger/conversations";

    [HttpGet]
    public async Task<ActionResult<ConversationListResource>> List(CancellationToken cancellationToken) =>
        new ConversationListResource
        {
            Items = [.. (await conversations.MineAsync(cancellationToken)).Select(Resource)],
            Links = [new Link("self", $"/{Base}", "GET"), new Link("open-direct", $"/{Base}/direct", "POST"), new Link("create-group", $"/{Base}", "POST")],
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
        await conversations.AddMemberAsync(id, new WrappedKey(request.UserId, request.Key), cancellationToken);
        return NoContent();
    }

    /// <summary>Leave (yourself) or remove (an administrator), with the next epoch's keys for whoever remains.</summary>
    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, [FromBody] RemoveMemberRequest request, CancellationToken cancellationToken)
    {
        await conversations.RemoveMemberAsync(id, userId, Keys(request.NextKeys), cancellationToken);
        return NoContent();
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
            // Each member carries the address that removes them; the caller's own is how they leave. A one-to-one chat
            // has neither: it stays between two people.
            Members = [.. view.Members.Select(m => new MemberResource
            {
                UserId = m,
                Links = view.IsDirect || !(view.IAmAdmin || m == view.Me) ? [] : [new Link("remove", $"{self}/members/{m}", "DELETE")],
            })],
            Admins = [.. view.Admins],
            Unread = view.Unread,
            CanManageMembers = !view.IsDirect && view.IAmAdmin,
            Links =
            [
                new Link("self", self, "GET"),
                new Link("messages", $"{self}/messages", "GET"),
                new Link("receipt", $"{self}/receipt", "PUT"),
                new Link("upload-file", $"{self}/files", "POST"),
                .. !view.IsDirect && view.IAmAdmin ? [new Link("add-member", $"{self}/members", "POST")] : Array.Empty<Link>(),
            ],
        };
    }
}
