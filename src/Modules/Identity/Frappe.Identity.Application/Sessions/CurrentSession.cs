using NodaTime;

namespace Frappe.Identity.Application.Sessions;

/// <summary>A session as its holder sees it.</summary>
/// <param name="Id">The session.</param>
/// <param name="IdleExpiresAt">The session idles out when unused by then.</param>
/// <param name="AbsoluteExpiresAt">The session ends by then, however much it is used.</param>
/// <param name="StepUpUntil">Until when a recent password check counts, if one was made (it may already be past).</param>
public sealed record CurrentSession(Guid Id, Instant IdleExpiresAt, Instant AbsoluteExpiresAt, Instant? StepUpUntil);
