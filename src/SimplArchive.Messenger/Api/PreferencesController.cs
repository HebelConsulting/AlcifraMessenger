using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SimplArchive.Messenger.ReadModel;
using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger.Api;

/// <summary>
/// The caller's own Messenger preferences (#26), followed from the conversation list's <c>preferences</c> link: GET reads
/// them with their tag, PUT replaces them whole under the If-Match of that read (one rel, the method says which).
/// </summary>
[ApiController]
[Authorize]
[Route(Base)]
public sealed class PreferencesController(Preferences preferences) : ControllerBase
{
    public const string Base = "api/modules/messenger/preferences";

    [HttpGet]
    public async Task<ActionResult<PreferencesResource>> Get(CancellationToken cancellationToken)
    {
        var mine = await preferences.MineAsync(cancellationToken);
        Response.Headers.ETag = Tag(mine);
        return Resource(mine);
    }

    // Standing convention: every GET has its own HEAD.
    [HttpHead]
    public async Task<IActionResult> Head(CancellationToken cancellationToken)
    {
        Response.Headers.ETag = Tag(await preferences.MineAsync(cancellationToken));
        return NoContent();
    }

    /// <summary>The caller's preferences, whole, with the If-Match of the read that filled the form.</summary>
    [HttpPut]
    public async Task<ActionResult<PreferencesResource>> Replace([FromBody] PreferencesRequest request, CancellationToken cancellationToken)
    {
        var stored = await preferences.ReplaceAsync(request.TypingIndicator, IfMatch(), cancellationToken);
        Response.Headers.ETag = Tag(stored);
        return Resource(stored);
    }

    private Guid? IfMatch() =>
        Request.Headers.IfMatch.ToString().Trim('"') is { Length: > 0 } raw && Guid.TryParse(raw, out var token) ? token : null;

    private static string Tag(UserPreferences preferences) => $"\"{preferences.ConcurrencyToken}\"";

    private static PreferencesResource Resource(UserPreferences preferences) => new()
    {
        TypingIndicator = preferences.TypingIndicator,
        ETag = Tag(preferences),
        Links = [new Link("self", $"/{Base}", "GET")],
    };
}
