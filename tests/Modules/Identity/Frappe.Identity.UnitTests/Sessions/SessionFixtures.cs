using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Sessions;
using NodaTime;

namespace Frappe.Identity.UnitTests.Sessions;

/// <summary>The sessions and principals the session tests share, with fixed values.</summary>
internal static class SessionFixtures
{
    public static readonly Instant Opened = Instant.FromUtc(2026, 11, 24, 12, 0);

    public static readonly Duration Idle = Duration.FromHours(12);

    public static readonly Duration Absolute = Duration.FromDays(30);

    public static Principal ActivePrincipal() => Principal.Create(PrincipalKind.Owner, PrincipalStatus.Active, Opened);

    public static Principal PendingPrincipal() => Principal.Create(PrincipalKind.Owner, PrincipalStatus.Pending, Opened);

    /// <summary>A session of <paramref name="principal"/> opened at <see cref="Opened"/> under the principal's current stamp.</summary>
    public static Session SessionOf(Principal principal, byte[]? tokenHash = null) =>
        Session.Open(
            principal.Id, tokenHash ?? new byte[32], SessionAudience.Console, principal.CredentialStamp, Opened, Idle, Absolute, "agent", null);
}
