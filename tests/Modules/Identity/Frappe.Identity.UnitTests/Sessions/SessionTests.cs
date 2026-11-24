using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Sessions;
using NodaTime;
using static Frappe.Identity.UnitTests.Sessions.SessionFixtures;

namespace Frappe.Identity.UnitTests.Sessions;

/// <summary>
/// A session lets its principal in only while it is alive (S2, S8): neither expiry reached, not revoked, the principal active and its credential
/// stamp the one the session was opened under. Using it moves the idle expiry on, never past the absolute one.
/// </summary>
public sealed class SessionTests
{
    [Test]
    public async Task A_session_opens_with_its_idle_and_absolute_expiry_and_raises_that_it_opened()
    {
        // Arrange
        var principal = ActivePrincipal();

        // Act
        var session = SessionOf(principal);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(session.IdleExpiresAt).IsEqualTo(Opened + Idle);
            await Assert.That(session.AbsoluteExpiresAt).IsEqualTo(Opened + Absolute);
            await Assert.That(session.LastSeenAt).IsEqualTo(Opened);
            await Assert.That(session.CredentialStamp).IsEqualTo(principal.CredentialStamp);
            await Assert.That(session.DomainEvents.Single()).IsEqualTo(new SessionOpened(principal.Id, session.Id, SessionAudience.Console, Opened));
        }
    }

    [Test]
    public async Task An_idle_timeout_longer_than_the_absolute_one_is_cut_to_it()
    {
        // Arrange
        var principal = ActivePrincipal();

        // Act
        var session = Session.Open(
            principal.Id, new byte[32], SessionAudience.Console, principal.CredentialStamp, Opened, Duration.FromDays(60), Absolute, null, null);

        // Assert
        await Assert.That(session.IdleExpiresAt).IsEqualTo(session.AbsoluteExpiresAt);
    }

    [Test]
    public async Task A_live_session_of_an_active_principal_lets_it_in()
    {
        // Arrange
        var principal = ActivePrincipal();
        var session = SessionOf(principal);

        // Act
        var active = session.IsActiveFor(Opened + Duration.FromHours(1), principal);

        // Assert
        await Assert.That(active).IsTrue();
    }

    [Test]
    public async Task A_session_unused_for_its_idle_timeout_is_no_longer_active_from_that_instant()
    {
        // Arrange
        var principal = ActivePrincipal();
        var session = SessionOf(principal);

        // Act
        var justBefore = session.IsActiveFor(Opened + Idle - Duration.FromTicks(1), principal);
        var atExpiry = session.IsActiveFor(Opened + Idle, principal);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(justBefore).IsTrue();
            await Assert.That(atExpiry).IsFalse();
        }
    }

    [Test]
    public async Task A_session_ends_at_its_absolute_expiry_however_recently_it_was_used()
    {
        // Arrange
        var principal = ActivePrincipal();
        var session = SessionOf(principal);
        var lastDay = Opened + Absolute - Duration.FromHours(1);
        session.Touch(lastDay, Idle);

        // Act
        var before = session.IsActiveFor(Opened + Absolute - Duration.FromTicks(1), principal);
        var atExpiry = session.IsActiveFor(Opened + Absolute, principal);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(before).IsTrue();
            await Assert.That(atExpiry).IsFalse();
        }
    }

    [Test]
    public async Task A_session_is_rejected_once_its_principal_changed_credentials()
    {
        // Arrange
        var principal = ActivePrincipal();
        var session = SessionOf(principal);
        var rotated = Session.Open(
            principal.Id, new byte[32], SessionAudience.Console, Guid.CreateVersion7(), Opened, Idle, Absolute, null, null);

        // Act
        var sameStamp = session.IsActiveFor(Opened, principal);
        var otherStamp = rotated.IsActiveFor(Opened, principal);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(sameStamp).IsTrue();
            await Assert.That(otherStamp).IsFalse();
        }
    }

    [Test]
    public async Task A_session_of_a_principal_that_is_not_active_is_rejected()
    {
        // Arrange
        var pending = PendingPrincipal();
        var session = SessionOf(pending);

        // Act
        var active = session.IsActiveFor(Opened, pending);

        // Assert
        await Assert.That(active).IsFalse();
    }

    [Test]
    public async Task A_session_does_not_let_another_principal_in()
    {
        // Arrange
        var session = SessionOf(ActivePrincipal());

        // Act
        var active = session.IsActiveFor(Opened, ActivePrincipal());

        // Assert
        await Assert.That(active).IsFalse();
    }

    [Test]
    public async Task A_revoked_session_is_rejected_and_records_why_and_when()
    {
        // Arrange
        var principal = ActivePrincipal();
        var session = SessionOf(principal);
        var at = Opened + Duration.FromMinutes(5);

        // Act
        session.Revoke(RevocationReason.RevokedByHolder, at);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(session.IsActiveFor(at, principal)).IsFalse();
            await Assert.That(session.RevokedAt).IsEqualTo(at);
            await Assert.That(session.RevocationReason).IsEqualTo(RevocationReason.RevokedByHolder);
        }
    }

    [Test]
    public async Task Logging_out_raises_that_the_session_closed_and_any_other_reason_raises_that_it_was_revoked()
    {
        // Arrange
        var principal = ActivePrincipal();
        var loggedOut = SessionOf(principal);
        var revoked = SessionOf(principal);
        loggedOut.DequeueDomainEvents();
        revoked.DequeueDomainEvents();
        var at = Opened + Duration.FromMinutes(5);

        // Act
        loggedOut.Revoke(RevocationReason.Logout, at);
        revoked.Revoke(RevocationReason.PasswordChanged, at);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(loggedOut.DomainEvents.Single()).IsEqualTo(new SessionClosed(principal.Id, loggedOut.Id, at));
            await Assert.That(revoked.DomainEvents.Single())
                .IsEqualTo(new SessionRevoked(principal.Id, revoked.Id, RevocationReason.PasswordChanged, at));
        }
    }

    [Test]
    public async Task Revoking_twice_changes_nothing_and_raises_nothing_more()
    {
        // Arrange
        var session = SessionOf(ActivePrincipal());
        session.Revoke(RevocationReason.Logout, Opened + Duration.FromMinutes(5));
        session.DequeueDomainEvents();

        // Act
        session.Revoke(RevocationReason.PasswordReset, Opened + Duration.FromMinutes(9));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(session.RevocationReason).IsEqualTo(RevocationReason.Logout);
            await Assert.That(session.RevokedAt).IsEqualTo(Opened + Duration.FromMinutes(5));
            await Assert.That(session.DomainEvents.Count).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Using_a_session_moves_its_idle_expiry_on_from_that_moment()
    {
        // Arrange
        var session = SessionOf(ActivePrincipal());
        var usedAt = Opened + Duration.FromHours(10);

        // Act
        session.Touch(usedAt, Idle);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(session.LastSeenAt).IsEqualTo(usedAt);
            await Assert.That(session.IdleExpiresAt).IsEqualTo(usedAt + Idle);
        }
    }

    [Test]
    public async Task Using_a_session_near_its_absolute_expiry_never_pushes_the_idle_expiry_past_it()
    {
        // Arrange
        var session = SessionOf(ActivePrincipal());
        var usedAt = Opened + Absolute - Duration.FromHours(1);

        // Act
        session.Touch(usedAt, Idle);

        // Assert
        await Assert.That(session.IdleExpiresAt).IsEqualTo(Opened + Absolute);
    }

    [Test]
    public async Task A_late_touch_never_moves_the_idle_expiry_back()
    {
        // Arrange
        var session = SessionOf(ActivePrincipal());
        session.Touch(Opened + Duration.FromHours(10), Idle);

        // Act
        session.Touch(Opened + Duration.FromHours(2), Idle);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(session.IdleExpiresAt).IsEqualTo(Opened + Duration.FromHours(10) + Idle);
            await Assert.That(session.LastSeenAt).IsEqualTo(Opened + Duration.FromHours(10));
        }
    }

    [Test]
    public async Task A_revoked_session_is_not_brought_back_by_using_it()
    {
        // Arrange
        var principal = ActivePrincipal();
        var session = SessionOf(principal);
        session.Revoke(RevocationReason.Logout, Opened + Duration.FromMinutes(5));

        // Act
        session.Touch(Opened + Duration.FromMinutes(6), Idle);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(session.LastSeenAt).IsEqualTo(Opened);
            await Assert.That(session.IdleExpiresAt).IsEqualTo(Opened + Idle);
        }
    }

    [Test]
    public async Task The_facts_of_an_ended_session_do_not_let_anyone_in()
    {
        // Arrange
        var principal = ActivePrincipal();
        var live = SessionOf(principal);
        var facts = new SessionAccess(
            live.Id, principal.Id, PrincipalKind.Owner, PrincipalStatus.Active, principal.CredentialStamp, principal.CredentialStamp,
            [AuthenticationMethod.Pwd], Opened, Opened + Idle, Opened + Absolute, Opened + Duration.FromMinutes(1), null, null);

        // Act
        var active = facts.IsActiveAt(Opened);

        // Assert
        await Assert.That(active).IsFalse();
    }

    [Test]
    public async Task Rotating_the_stamp_ends_every_session_opened_under_the_old_one_except_one_given_the_new_stamp()
    {
        // Arrange
        var principal = ActivePrincipal();
        var stale = SessionOf(principal);
        var followed = SessionOf(principal);

        // Act
        principal.RotateStamp();
        followed.Restamp(principal.CredentialStamp);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(stale.IsActiveFor(Opened, principal)).IsFalse();
            await Assert.That(followed.IsActiveFor(Opened, principal)).IsTrue();
        }
    }

    [Test]
    public async Task A_revoked_session_is_not_restamped()
    {
        // Arrange
        var principal = ActivePrincipal();
        var session = SessionOf(principal);
        var opened = session.CredentialStamp;
        session.Revoke(RevocationReason.Logout, Opened + Duration.FromMinutes(1));

        // Act
        session.Restamp(Guid.CreateVersion7());

        // Assert
        await Assert.That(session.CredentialStamp).IsEqualTo(opened);
    }

    [Test]
    public async Task Stepping_up_opens_a_five_minute_window_from_completion_that_use_never_extends_and_a_new_step_up_renews()
    {
        // Arrange
        var session = SessionOf(ActivePrincipal());
        session.DequeueDomainEvents();
        var at = Opened + Duration.FromMinutes(1);

        // Act
        var stepped = session.StepUp(at);
        var until = session.StepUpUntil;
        session.Touch(at + Duration.FromMinutes(4), Duration.FromHours(12));
        var afterUse = session.StepUpUntil;
        var renewed = session.StepUp(at + Duration.FromMinutes(4));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(stepped).IsTrue();
            await Assert.That(until).IsEqualTo(at + Duration.FromMinutes(5));
            await Assert.That(afterUse).IsEqualTo(until);
            await Assert.That(renewed).IsTrue();
            await Assert.That(session.StepUpUntil).IsEqualTo(at + Duration.FromMinutes(9));
            await Assert.That(session.DomainEvents.OfType<StepUpCompleted>().Select(e => e.ValidUntil))
                .IsEquivalentTo([at + Duration.FromMinutes(5), at + Duration.FromMinutes(9)]);
        }
    }

    [Test]
    public async Task A_revoked_session_does_not_step_up()
    {
        // Arrange
        var session = SessionOf(ActivePrincipal());
        session.Revoke(RevocationReason.Logout, Opened + Duration.FromMinutes(1));
        session.DequeueDomainEvents();

        // Act
        var stepped = session.StepUp(Opened + Duration.FromMinutes(2));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(stepped).IsFalse();
            await Assert.That(session.StepUpUntil).IsNull();
            await Assert.That(session.DomainEvents).IsEmpty();
        }
    }
}
