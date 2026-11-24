namespace Frappe.Identity.Http.Sessions;

/// <summary>The session a request is using, as its holder sees it.</summary>
/// <param name="Id">The session.</param>
/// <param name="IdleExpiresAt">The session idles out when it is not used by then; each use moves it on, never past the absolute expiry.</param>
/// <param name="AbsoluteExpiresAt">The session ends by then, however much it is used.</param>
/// <param name="StepUpValidUntil">Until when a recent password check still counts; null when none was made or it has run out.</param>
public sealed record CurrentSessionResponse(
    Guid Id, DateTimeOffset IdleExpiresAt, DateTimeOffset AbsoluteExpiresAt, DateTimeOffset? StepUpValidUntil);
