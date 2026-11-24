using NodaTime;

namespace Frappe.Identity.Application.Sessions;

/// <summary>Where a page of sessions ended, in the order they are listed: newest opened first, then by id.</summary>
/// <param name="OpenedAt">When the last session of the page opened.</param>
/// <param name="Id">The last session of the page, which breaks a tie between sessions opened at the same instant.</param>
public sealed record SessionPosition(Instant OpenedAt, Guid Id);
