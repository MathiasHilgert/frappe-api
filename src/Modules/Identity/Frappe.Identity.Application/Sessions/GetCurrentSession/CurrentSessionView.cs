using NodaTime;

namespace Frappe.Identity.Application.Sessions.GetCurrentSession;

/// <summary>A session as its holder sees it.</summary>
/// <param name="Id">The session.</param>
/// <param name="IdleExpiresAt">The session idles out when unused by then.</param>
/// <param name="AbsoluteExpiresAt">The session ends by then, however much it is used.</param>
/// <param name="StepUpValidUntil">Until when a recent password check still counts; none when none was made or it has run out.</param>
public sealed record CurrentSessionView(Guid Id, Instant IdleExpiresAt, Instant AbsoluteExpiresAt, Instant? StepUpValidUntil);
