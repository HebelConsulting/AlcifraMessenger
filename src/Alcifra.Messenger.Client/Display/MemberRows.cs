using Alcifra.Messenger.Client.Api;

namespace Alcifra.Messenger.Client.Display;

/// <summary>A member as the members page draws them, and what the caller may do with them — from the server's links.</summary>
public sealed record MemberRow(Member Member, string Name, bool IsMe, bool IsAdmin, bool CanMakeAdmin, bool CanRevokeAdmin, bool CanRemove, bool CanLeave)
{
    /// <summary>Whether tapping the row offers anything at all.</summary>
    public bool HasActions => CanMakeAdmin || CanRevokeAdmin || CanRemove || CanLeave;
}

/// <summary>A group's members: administrators first, then by name; the caller's own row offers leaving, others what the
/// server's links allow (core #1810).</summary>
public static class MemberRows
{
    /// <summary>The rows of <paramref name="group"/> for <paramref name="me"/>.</summary>
    public static IReadOnlyList<MemberRow> Build(Conversation group, Guid me) =>
        [.. group.Members
            .Select(m =>
            {
                var isMe = m.UserId == me;
                var name = isMe ? ViewText.Get("You") : m.DisplayName is { Length: > 0 } n ? n : "?";
                return new MemberRow(m, name, isMe, m.IsAdmin,
                    CanMakeAdmin: m.Offers("make-admin"),
                    CanRevokeAdmin: m.Offers("revoke-admin"),
                    CanRemove: !isMe && m.Offers("remove"),
                    CanLeave: isMe && m.Offers("remove"));
            })
            .OrderByDescending(r => r.IsAdmin)
            .ThenBy(r => r.IsMe ? 0 : 1)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)];
}
