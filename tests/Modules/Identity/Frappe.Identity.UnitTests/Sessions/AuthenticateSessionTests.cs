using Frappe.Identity.Application.Sessions.AuthenticateSession;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Sessions;
using Frappe.Results;
using Microsoft.Extensions.Time.Testing;
using NodaTime;
using static Frappe.Identity.UnitTests.Sessions.SessionFixtures;

namespace Frappe.Identity.UnitTests.Sessions;

/// <summary>
/// Who a session token proves (S8): a live session of an active principal, read together on every request; anything else is the one
/// <c>identity.session.invalid</c>. The query only reads (CQRS-07).
/// </summary>
public sealed class AuthenticateSessionTests
{
    private static readonly byte[] Hash = [.. Enumerable.Repeat((byte)7, 32)];

    private static async Task<Result<SessionPrincipal>> AuthenticateAsync(FakeSessions store, Duration after, byte[]? hash = null)
    {
        var time = new FakeTimeProvider(Opened.ToDateTimeOffset() + after.ToTimeSpan());
        return await new AuthenticateSessionHandler(store, time).HandleAsync(new AuthenticateSession(hash ?? Hash), default);
    }

    private static async Task AssertInvalidAsync(Result<SessionPrincipal> result)
    {
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Errors.Single()).IsEqualTo(new UnauthenticatedError(IdentityErrorCodes.SessionInvalid));
        }
    }

    [Test]
    public async Task A_live_session_of_an_active_principal_proves_the_principal_the_session_and_how_it_was_proved()
    {
        // Arrange
        var store = new FakeSessions();
        var (principal, session) = store.Seed(Hash);

        // Act
        var result = await AuthenticateAsync(store, Duration.FromHours(1));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Value.PrincipalId).IsEqualTo(principal.Id);
            await Assert.That(result.Value.Kind).IsEqualTo("owner");
            await Assert.That(result.Value.SessionId).IsEqualTo(session.Id);
            await Assert.That(result.Value.Methods.Single()).IsEqualTo("pwd");
            await Assert.That(result.Value.LastSeenAt).IsEqualTo(Opened);
            await Assert.That(result.Value.StepUpUntil).IsNull();
        }
    }

    [Test]
    public async Task A_token_nobody_holds_is_invalid()
    {
        // Arrange
        var store = new FakeSessions();
        store.Seed(Hash);

        // Act
        var result = await AuthenticateAsync(store, Duration.Zero, [.. Enumerable.Repeat((byte)9, 32)]);

        // Assert
        await AssertInvalidAsync(result);
    }

    [Test]
    public async Task A_session_unused_for_its_idle_timeout_is_invalid()
    {
        // Arrange
        var store = new FakeSessions();
        store.Seed(Hash);

        // Act
        var result = await AuthenticateAsync(store, Idle);

        // Assert
        await AssertInvalidAsync(result);
    }

    [Test]
    public async Task A_session_past_its_absolute_expiry_is_invalid()
    {
        // Arrange
        var store = new FakeSessions();
        var (_, session) = store.Seed(Hash);
        session.Touch(Opened + Absolute - Duration.FromHours(1), Idle);

        // Act
        var result = await AuthenticateAsync(store, Absolute);

        // Assert
        await AssertInvalidAsync(result);
    }

    [Test]
    public async Task A_session_whose_principal_changed_credentials_is_invalid_at_once()
    {
        // Arrange
        var store = new FakeSessions();
        var (principal, session) = store.Seed(Hash);
        store.Principals[store.Principals.IndexOf(principal)] = Principal.Create(PrincipalKind.Owner, PrincipalStatus.Active, Opened);
        store.Stored[store.Stored.IndexOf(session)] = Session.Open(
            store.Principals[0].Id, Hash, SessionAudience.Console, Guid.CreateVersion7(), Opened, Idle, Absolute, null, null);

        // Act
        var result = await AuthenticateAsync(store, Duration.FromMinutes(1));

        // Assert
        await AssertInvalidAsync(result);
    }

    [Test]
    public async Task A_session_of_a_principal_that_is_not_active_is_invalid()
    {
        // Arrange
        var store = new FakeSessions();
        store.Seed(Hash, PrincipalStatus.Pending);

        // Act
        var result = await AuthenticateAsync(store, Duration.FromMinutes(1));

        // Assert
        await AssertInvalidAsync(result);
    }

    [Test]
    public async Task A_revoked_session_is_invalid()
    {
        // Arrange
        var store = new FakeSessions();
        var (_, session) = store.Seed(Hash);
        session.Revoke(RevocationReason.Logout, Opened + Duration.FromMinutes(1));

        // Act
        var result = await AuthenticateAsync(store, Duration.FromMinutes(2));

        // Assert
        await AssertInvalidAsync(result);
    }

    [Test]
    public async Task A_session_is_due_for_a_touch_only_when_it_was_last_seen_a_minute_ago_or_more()
    {
        // Arrange
        var store = new FakeSessions();
        store.Seed(Hash);

        // Act
        var fresh = (await AuthenticateAsync(store, Duration.FromSeconds(59))).Value;
        var stale = (await AuthenticateAsync(store, Duration.FromMinutes(1))).Value;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(fresh.IsDueForTouch(Opened + Duration.FromSeconds(59))).IsFalse();
            await Assert.That(stale.IsDueForTouch(Opened + Duration.FromMinutes(1))).IsTrue();
        }
    }

    [Test]
    public async Task The_query_masks_the_hash_it_carries_when_printed()
    {
        // Act
        var text = new AuthenticateSession(Hash).ToString();

        // Assert
        await Assert.That(text).Contains("***");
    }
}
