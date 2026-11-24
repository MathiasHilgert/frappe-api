using Frappe.Identity.Domain.Sessions;
using Frappe.Identity.Infrastructure.Messaging;
using NodaTime;
using static Frappe.Identity.UnitTests.Sessions.SessionFixtures;
using Published = Frappe.Identity.Contracts.Sessions;

namespace Frappe.Identity.UnitTests.Sessions;

/// <summary>How a session ends in the sweep (S2, S16, S17): by its idle or absolute expiry with a fact, or silently once it was revoked.</summary>
public sealed class SessionEndTests
{
    private static readonly IdentityIntegrationEvents Translator = new();

    private static Session Open()
    {
        var session = SessionOf(ActivePrincipal());
        session.DequeueDomainEvents();
        return session;
    }

    [Test]
    public async Task A_session_that_idled_out_ends_and_raises_the_idle_fact()
    {
        // Arrange
        var session = Open();
        var now = Opened + Idle + Duration.FromMinutes(1);

        // Act
        session.End(now);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(session.EndedAt).IsEqualTo(now);
            await Assert.That(session.DomainEvents.Single()).IsEqualTo(new SessionEnded(session.PrincipalId, session.Id, SessionEndReason.Idle, now));
        }
    }

    [Test]
    public async Task A_session_used_until_its_absolute_lifetime_ends_with_the_absolute_fact()
    {
        // Arrange
        var session = SessionOf(ActivePrincipal());
        session.Touch(Opened + Absolute - Duration.FromHours(1), Idle);
        session.DequeueDomainEvents();
        var now = Opened + Absolute;

        // Act
        session.End(now);

        // Assert
        await Assert.That(session.DomainEvents.Single()).IsEqualTo(new SessionEnded(session.PrincipalId, session.Id, SessionEndReason.Absolute, now));
    }

    [Test]
    public async Task A_revoked_session_ends_silently_never_before_its_revocation()
    {
        // Arrange
        var session = Open();
        var revokedAt = Opened + Duration.FromHours(1);
        session.Revoke(RevocationReason.OthersRevoked, revokedAt);
        session.DequeueDomainEvents();

        // Act
        session.End(Opened + Duration.FromMinutes(30));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(session.EndedAt).IsEqualTo(revokedAt);
            await Assert.That(session.DomainEvents).IsEmpty();
        }
    }

    [Test]
    public async Task A_session_that_ended_already_keeps_its_end_and_raises_nothing_more()
    {
        // Arrange
        var session = Open();
        var first = Opened + Idle + Duration.FromMinutes(1);
        session.End(first);
        session.DequeueDomainEvents();

        // Act
        session.End(first + Duration.FromDays(1));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(session.EndedAt).IsEqualTo(first);
            await Assert.That(session.DomainEvents).IsEmpty();
        }
    }

    [Test]
    public async Task A_session_seen_by_the_sweep_ends_with_the_same_fact_as_a_loaded_one()
    {
        // Arrange
        var loaded = Open();
        var seen = Session.SeenBySweep(loaded.Id, loaded.PrincipalId, loaded.IdleExpiresAt, loaded.AbsoluteExpiresAt, null);
        var now = Opened + Idle + Duration.FromMinutes(1);

        // Act
        seen.End(now);

        // Assert
        await Assert.That(seen.DomainEvents.Single()).IsEqualTo(new SessionEnded(loaded.PrincipalId, loaded.Id, SessionEndReason.Idle, now));
    }

    [Test]
    public async Task The_fact_is_published_with_its_reason_spelled_as_stored()
    {
        // Arrange
        var session = Open();
        var now = Opened + Idle + Duration.FromMinutes(1);
        session.End(now);

        // Act
        var published = session.DomainEvents.SelectMany(Translator.Translate).Single();

        // Assert
        await Assert.That(published).IsEqualTo(new Published.SessionEnded(session.PrincipalId, session.Id, "idle", now.ToDateTimeOffset()));
    }
}
