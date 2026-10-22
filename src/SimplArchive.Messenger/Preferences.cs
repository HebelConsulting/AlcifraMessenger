using Microsoft.EntityFrameworkCore;
using SimplArchive.Messenger.ReadModel;
using SimplArchive.ModuleAbi;

namespace SimplArchive.Messenger;

/// <summary>
/// The caller's own Messenger preferences (#26): read, and replaced whole under the tag the caller read. A user without a
/// row has the defaults, under the tag <see cref="Guid.Empty"/>.
/// </summary>
public sealed class Preferences(MessengerReadModelContext db, IModuleCallerContext caller)
{
    /// <summary>The caller's preferences as stored, or the defaults (not stored) under <see cref="Guid.Empty"/>.</summary>
    public async Task<UserPreferences> MineAsync(CancellationToken cancellationToken)
    {
        var me = Me;
        return await db.UserPreferences.AsNoTracking()
                .FirstOrDefaultAsync(p => p.TenantId == caller.TenantId && p.UserId == me, cancellationToken)
            ?? new UserPreferences { TenantId = caller.TenantId, UserId = me };
    }

    /// <summary>Whether the caller's typing indicator is on: the default, unless they switched it off.</summary>
    public async Task<bool> TypingIndicatorAsync(CancellationToken cancellationToken) => (await MineAsync(cancellationToken)).TypingIndicator;

    /// <summary>
    /// Of <paramref name="users"/>, those whose typing indicator is off. Read in one query for a whole conversation.
    /// </summary>
    public async Task<IReadOnlySet<Guid>> SwitchedOffAsync(IReadOnlyCollection<Guid> users, CancellationToken cancellationToken) =>
        (await db.UserPreferences.AsNoTracking()
            .Where(p => p.TenantId == caller.TenantId && users.Contains(p.UserId) && !p.TypingIndicator)
            .Select(p => p.UserId).ToListAsync(cancellationToken)).ToHashSet();

    /// <summary>
    /// Replaces the caller's preferences with <paramref name="typingIndicator"/>, provided they are still what the caller
    /// read: <paramref name="ifMatch"/> is that read's tag (<see cref="Guid.Empty"/> for the defaults). Returns the stored row.
    /// </summary>
    public async Task<UserPreferences> ReplaceAsync(bool typingIndicator, Guid? ifMatch, CancellationToken cancellationToken)
    {
        var me = Me;
        var seen = ifMatch ?? throw MessengerErrors.IfMatchRequired();
        var row = await db.UserPreferences.FirstOrDefaultAsync(p => p.TenantId == caller.TenantId && p.UserId == me, cancellationToken);
        if (row is null)
        {
            if (seen != Guid.Empty)
            {
                throw MessengerErrors.PreferencesChanged();   // a tag of a row that is not there: somebody's read nobody can have had
            }

            row = new UserPreferences { TenantId = caller.TenantId, UserId = me };
            db.UserPreferences.Add(row);
        }
        else
        {
            // The precondition is the tag the caller SAW, compared through EF so a change in between fails.
            db.Entry(row).Property(p => p.ConcurrencyToken).OriginalValue = seen;
        }

        (row.TypingIndicator, row.ConcurrencyToken) = (typingIndicator, Guid.NewGuid());
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)   // a concurrency failure, or another device's first write of the row in between
        {
            throw MessengerErrors.PreferencesChanged();
        }

        return row;
    }

    private Guid Me => caller.UserId ?? throw MessengerErrors.NotSignedIn();
}
