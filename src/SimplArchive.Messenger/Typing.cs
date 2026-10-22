using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SimplArchive.Messenger.ReadModel;
using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger;

/// <summary>
/// "Anna is typing…" (#26): a member says they started or stopped typing in a conversation, and the other members' open
/// apps are told over the core's live hub (ABI 1.25, core ADR 0957) — never stored, never pushed to a closed app. The
/// server learns THAT someone types, never what.
/// </summary>
/// <remarks>
/// The indicator is reciprocal (owner decision 2026-10-21): a sender who switched it off sends nothing at all, and a member
/// who switched it off is not told. Best effort throughout: whatever the hub refuses (a rate spent, the module not
/// permitted) is dropped, because a typing state is worthless a few seconds later.
/// </remarks>
public sealed class Typing(
    MessengerReadModelContext db, Conversations conversations, Preferences preferences, TypingThrottle throttle,
    IModuleLiveEvents live, IModuleCallerContext caller, ILogger<Typing> logger)
{
    /// <summary>The live event's name, which the Messenger's clients branch on.</summary>
    public const string EventName = "typing";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The caller started (<paramref name="typing"/>) or stopped typing in <paramref name="conversationId"/>.</summary>
    /// <returns>Whether an event went out — for the tests; a client is answered alike either way.</returns>
    public async Task<bool> SetAsync(Guid conversationId, bool typing, CancellationToken cancellationToken)
    {
        var me = conversations.Me;
        var members = await MembersAsync(conversationId, cancellationToken);
        if (!members.Contains(me))
        {
            throw MessengerErrors.NotAMember();
        }

        if (!await preferences.TypingIndicatorAsync(cancellationToken))
        {
            return false;   // switched off: the sender's typing is shown to nobody
        }

        if (typing && !throttle.LetsStartThrough(caller.TenantId, me, conversationId))
        {
            return false;   // a start went out within the interval: the members' apps still show it
        }

        if (!typing)
        {
            throttle.Stopped(caller.TenantId, me, conversationId);
        }

        var others = members.Where(m => m != me).ToList();
        var switchedOff = await preferences.SwitchedOffAsync(others, cancellationToken);
        var recipients = others.Where(m => !switchedOff.Contains(m)).Take(ModuleLiveEvent.MaxRecipients).ToList();
        if (recipients.Count == 0)
        {
            return false;
        }

        var payload = JsonSerializer.Serialize(new TypingPayload(conversationId, me, typing), Json);
        try
        {
            await live.SendAsync(new ModuleLiveEvent(recipients, EventName, payload), cancellationToken);
            return true;
        }
        catch (ModuleLiveEventRefusedException refused)
        {
            // Dropped: a typing state is not worth a retry. Debug, since the core logs its own refusal.
            logger.LogDebug("A typing event in {ConversationId} was dropped: {ErrorCode}", conversationId, refused.ErrorCode);
            return false;
        }
    }

    // The members now: a key at the current epoch its holder has not left. Restored history has no typing: it is read, not
    // written in.
    private async Task<IReadOnlyList<Guid>> MembersAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        if (await db.RestoredConversations.AnyAsync(r => r.ConversationId == conversationId, cancellationToken))
        {
            throw MessengerErrors.ReadOnly();
        }

        var epoch = await conversations.CurrentEpochAsync(conversationId, cancellationToken);
        return await db.EpochKeys.AsNoTracking()
            .Where(k => k.ConversationId == conversationId && k.Epoch == epoch && k.LeftAt == null)
            .Select(k => k.UserId).ToListAsync(cancellationToken);
    }

    /// <summary>What the members' apps receive: who types, or stopped typing, where.</summary>
    public sealed record TypingPayload(Guid Conversation, Guid User, bool Typing);
}
