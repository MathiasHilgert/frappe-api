using System.Security.Claims;
using Frappe.Identity.Application.Sessions.AuthenticateSession;
using Frappe.Identity.Http.Sessions;
using Microsoft.AspNetCore.Http;
using NodaTime;

namespace Frappe.Api.IntegrationTests;

/// <summary>
/// The caller of a web request (S6): read lazily from <c>HttpContext.User</c>, only for an identity of the session scheme, and never kept as
/// anonymous, so a request authenticated after the caller was first read is still seen as it is.
/// </summary>
public sealed class IdentitySessionCallerTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 24, 12, 0);

    private static readonly SessionPrincipal Session = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), "owner", Guid.Parse("33333333-3333-3333-3333-333333333333"), ["pwd"], Now,
        Now + Duration.FromMinutes(5));

    private static (SessionCaller Caller, HttpContextAccessor Accessor) CallerOver(ClaimsPrincipal? user)
    {
        var accessor = new HttpContextAccessor { HttpContext = user is null ? null : new DefaultHttpContext { User = user } };
        return (new SessionCaller(accessor), accessor);
    }

    [Test]
    public async Task A_session_identity_gives_the_principal_kind_session_and_step_up_of_the_session()
    {
        // Arrange
        var (caller, _) = CallerOver(SessionClaims.PrincipalOf(Session, Now));

        // Act
        var seen = (caller.PrincipalId, caller.Kind, caller.SessionId, caller.StepUpValidUntil, caller.IsSystem);

        // Assert
        var stepUp = (DateTimeOffset?)Session.StepUpUntil!.Value.ToDateTimeOffset();
        await Assert.That(seen).IsEqualTo((Session.PrincipalId, "owner", Session.SessionId, stepUp, false));
    }

    [Test]
    public async Task Without_an_HttpContext_the_caller_is_anonymous()
    {
        // Arrange
        var (caller, _) = CallerOver(user: null);

        // Act
        var seen = (caller.PrincipalId, caller.Kind, caller.SessionId, caller.StepUpValidUntil, caller.IsSystem);

        // Assert
        await Assert.That(seen).IsEqualTo((null, null, null, null, false));
    }

    [Test]
    public async Task An_identity_of_another_scheme_proves_nothing_even_with_the_same_claims()
    {
        // Arrange
        var other = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(Frappe.Http.SessionPolicy.PrincipalClaim, Session.PrincipalId.ToString()), new Claim(SessionClaims.KindClaim, "owner")],
            authenticationType: "Bearer"));
        var (caller, _) = CallerOver(other);

        // Act
        var principal = caller.PrincipalId;

        // Assert
        await Assert.That(principal).IsNull();
    }

    [Test]
    public async Task A_caller_read_as_anonymous_is_looked_up_again_once_the_session_authenticates()
    {
        // Arrange
        var (caller, accessor) = CallerOver(new ClaimsPrincipal(new ClaimsIdentity()));
        var before = caller.PrincipalId;

        // Act
        accessor.HttpContext!.User = SessionClaims.PrincipalOf(Session, Now);
        var after = caller.PrincipalId;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(before).IsNull();
            await Assert.That(after).IsEqualTo(Session.PrincipalId);
        }
    }

    [Test]
    public async Task An_authenticated_caller_is_kept_for_the_rest_of_the_scope()
    {
        // Arrange
        var (caller, accessor) = CallerOver(SessionClaims.PrincipalOf(Session, Now));
        var first = caller.PrincipalId;

        // Act
        accessor.HttpContext = null;
        var second = caller.PrincipalId;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first).IsEqualTo(Session.PrincipalId);
            await Assert.That(second).IsEqualTo(Session.PrincipalId);
        }
    }

    [Test]
    public async Task A_session_that_never_stepped_up_has_no_step_up_window()
    {
        // Arrange
        var (caller, _) = CallerOver(SessionClaims.PrincipalOf(Session with { StepUpUntil = null }, Now));

        // Act
        var until = caller.StepUpValidUntil;

        // Assert
        await Assert.That(until).IsNull();
    }
}
