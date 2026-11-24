using System.Security.Claims;
using Frappe.Http;
using Frappe.Identity.Application.Sessions.AuthenticateSession;
using Frappe.Identity.Http.Sessions;
using NodaTime;

namespace Frappe.Api.IntegrationTests;

/// <summary>
/// The claims a session gives a request: who, which session, what kind, how it was proved, and a step-up only while it counts (S8, S14).
/// </summary>
public sealed class IdentitySessionClaimsTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 24, 12, 0);

    private static SessionPrincipal Principal(Instant? stepUpUntil) =>
        new(Guid.Parse("0198f2a0-0000-7000-8000-000000000001"), "owner", Guid.Parse("0198f2a0-0000-7000-8000-000000000002"),
            ["pwd"], Now, stepUpUntil);

    [Test]
    public async Task A_session_gives_its_principal_session_kind_and_methods_as_claims_and_reads_back_through_the_platform_helpers()
    {
        // Act
        var user = SessionClaims.PrincipalOf(Principal(null), Now);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(user.Identity?.IsAuthenticated).IsTrue();
            await Assert.That(user.FindFirstValue("sub")).IsEqualTo("0198f2a0-0000-7000-8000-000000000001");
            await Assert.That(user.FindFirstValue("sid")).IsEqualTo("0198f2a0-0000-7000-8000-000000000002");
            await Assert.That(user.FindFirstValue("kind")).IsEqualTo("owner");
            await Assert.That(user.FindAll("amr").Select(c => c.Value).Single()).IsEqualTo("pwd");
            await Assert.That(SessionPolicy.PrincipalIdOf(user)).IsEqualTo(Guid.Parse("0198f2a0-0000-7000-8000-000000000001"));
            await Assert.That(SessionClaims.SessionIdOf(user)).IsEqualTo(Guid.Parse("0198f2a0-0000-7000-8000-000000000002"));
            await Assert.That(SessionPolicy.StepUpValidUntilOf(user)).IsNull();
        }
    }

    [Test]
    public async Task A_step_up_that_still_counts_is_a_claim_and_one_that_ran_out_is_not()
    {
        // Act
        var counting = SessionClaims.PrincipalOf(Principal(Now + Duration.FromMinutes(3)), Now);
        var ranOut = SessionClaims.PrincipalOf(Principal(Now), Now);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(SessionPolicy.StepUpValidUntilOf(counting)).IsEqualTo((Now + Duration.FromMinutes(3)).ToDateTimeOffset());
            await Assert.That(SessionPolicy.StepUpValidUntilOf(ranOut)).IsNull();
        }
    }

    [Test]
    public async Task The_session_of_a_user_that_never_authenticated_is_nothing()
    {
        // Arrange
        var claim = new Claim(SessionClaims.SessionClaim, Guid.CreateVersion7().ToString());

        // Act
        var session = SessionClaims.SessionIdOf(new ClaimsPrincipal(new ClaimsIdentity([claim])));

        // Assert
        await Assert.That(session).IsNull();
    }
}
