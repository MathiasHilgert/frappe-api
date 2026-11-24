using NodaTime;

namespace Frappe.Identity.Application.Sessions;

/// <summary>
/// How long a session lives (S8), as configured: unused it idles out after <paramref name="Idle"/>, used it ends after
/// <paramref name="Absolute"/>.
/// </summary>
/// <param name="Idle">The time a session may go unused; positive and not longer than <paramref name="Absolute"/>.</param>
/// <param name="Absolute">The time from opening after which a session ends however much it is used; positive.</param>
public sealed record SessionLifetimes(Duration Idle, Duration Absolute);
