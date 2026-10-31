using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SimplArchive.ModuleAbi;

namespace Alcifra.Messenger.Api;

/// <summary>
/// The people a conversation can include: active users with a usable enrolled certificate, and the certificates a client
/// seals a conversation key to. Reached from the root's <c>messenger:people</c> rel.
/// </summary>
[ApiController]
[Authorize]
[Route(Base)]
public sealed class PeopleController(IModuleArchiveFacade archive, Conversations conversations) : ControllerBase
{
    public const string Base = "api/modules/messenger/people";

    [HttpGet]
    public async Task<ActionResult<PeopleResource>> Search([FromQuery] string? term, CancellationToken cancellationToken)
    {
        _ = conversations.Me;   // a person asks, never a service account
        var people = await archive.SearchUsersAsync(term ?? string.Empty, 20, cancellationToken);
        return new PeopleResource
        {
            Items = [.. people.Select(p => new PersonResource
            {
                Id = p.Id,
                DisplayName = p.DisplayName,
                Email = p.Email,
                Links = [new Link("certificates", $"/{Base}/{p.Id}/certificates", "GET")],
            })],
            Links = [new Link("self", $"/{Base}", "GET")],
        };
    }

    // Standing convention: every GET has its own HEAD.
    [HttpHead]
    public IActionResult Head()
    {
        _ = conversations.Me;
        return NoContent();
    }

    [HttpGet("{userId:guid}/certificates")]
    public async Task<ActionResult<CertificatesResource>> Certificates(Guid userId, CancellationToken cancellationToken)
    {
        _ = conversations.Me;
        return new CertificatesResource
        {
            CertificatesPem = [.. (await archive.GetUserCertificatesAsync(userId, cancellationToken)).Select(c => c.CertificatePem)],
            Links = [new Link("self", $"/{Base}/{userId}/certificates", "GET")],
        };
    }

    [HttpHead("{userId:guid}/certificates")]
    public IActionResult HeadCertificates(Guid userId)
    {
        _ = conversations.Me;
        return NoContent();
    }
}
