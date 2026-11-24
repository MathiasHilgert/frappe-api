using Frappe.Identity.Application.Sessions;
using NodaTime;

namespace Frappe.Identity.Http.Sessions;

/// <summary>
/// Where a page of the session list ended, as it travels inside the opaque cursor: when the last session opened and its id. An instant that
/// the database cannot take makes the cursor invalid (<see cref="IsValid"/>), never a failure further down.
/// </summary>
/// <param name="OpenedAt">When the last session of the page opened (UTC).</param>
/// <param name="Id">The last session of the page.</param>
internal sealed record SessionCursorPosition(DateTimeOffset OpenedAt, Guid Id)
{
    public static SessionPosition ToApplication(SessionCursorPosition position) =>
        new(Instant.FromDateTimeOffset(position.OpenedAt), position.Id);

    public static SessionCursorPosition Of(SessionPosition position) => new(position.OpenedAt.ToDateTimeOffset(), position.Id);

    /// <summary>Whether the position is one a list of sessions can have ended at: a UTC instant in the years the database holds.</summary>
    public static bool IsValid(SessionCursorPosition position) =>
        position.OpenedAt.Offset == TimeSpan.Zero && position.OpenedAt.Year is >= 2000 and <= 9000;
}
