using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Sessions;
using Frappe.Identity.UnitTests.Sessions;
using Microsoft.Extensions.Time.Testing;
using NodaTime;
using static Frappe.Identity.UnitTests.Sessions.SessionFixtures;

namespace Frappe.Identity.UnitTests.Credentials;

/// <summary>
/// A credential change rotates the stamp, keeps the holder's own session alive and revokes the others, each through its aggregate (S2, S12).
/// </summary>
public sealed class CredentialChangeTests
{
    private readonly FakeSessions _sessions = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 24, 13, 0, 0, TimeSpan.Zero));

    private CredentialChange Change() => new(_sessions, _time);

    private (Principal Principal, Session Kept, Session Other) Seed()
    {
        var principal = ActivePrincipal();
        _sessions.Principals.Add(principal);
        var kept = SessionOf(principal, [1]);
        var other = SessionOf(principal, [2]);
        _sessions.Stored.AddRange([kept, other]);
        kept.DequeueDomainEvents();
        other.DequeueDomainEvents();
        return (principal, kept, other);
    }

    [Test]
    public async Task The_stamp_rotates_the_kept_session_follows_it_and_every_other_is_revoked_with_its_event()
    {
        // Arrange
        var (principal, kept, other) = Seed();
        var oldStamp = principal.CredentialStamp;
        var now = Instant.FromUtc(2026, 11, 24, 13, 0);

        // Act
        await Change().ApplyAsync(principal, RevocationReason.PasswordChanged, kept.Id, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(principal.CredentialStamp).IsNotEqualTo(oldStamp);
            await Assert.That(kept.CredentialStamp).IsEqualTo(principal.CredentialStamp);
            await Assert.That(kept.IsActiveFor(now, principal)).IsTrue();
            await Assert.That(kept.RevokedAt).IsNull();
            await Assert.That(kept.DomainEvents).IsEmpty();
            await Assert.That(other.IsActiveFor(now, principal)).IsFalse();
            await Assert.That(other.RevokedAt).IsEqualTo(now);
            await Assert.That(other.RevocationReason).IsEqualTo(RevocationReason.PasswordChanged);
            await Assert.That(other.DomainEvents.Single())
                .IsEqualTo(new SessionRevoked(principal.Id, other.Id, RevocationReason.PasswordChanged, now));
        }
    }

    [Test]
    public async Task A_session_past_its_idle_expiry_that_nobody_swept_is_left_alone()
    {
        // Arrange
        var (principal, kept, stale) = Seed();
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 25, 1, 0, 0, TimeSpan.Zero));

        // Act
        await Change().ApplyAsync(principal, RevocationReason.PasswordChanged, kept.Id, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(stale.RevokedAt).IsNull();
            await Assert.That(stale.DomainEvents).IsEmpty();
        }
    }

    [Test]
    public async Task With_no_session_to_keep_every_session_is_revoked_for_the_reason()
    {
        // Arrange
        var (principal, kept, other) = Seed();

        // Act
        await Change().ApplyAsync(principal, RevocationReason.PasswordReset, keptSessionId: null, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(kept.RevocationReason).IsEqualTo(RevocationReason.PasswordReset);
            await Assert.That(other.RevocationReason).IsEqualTo(RevocationReason.PasswordReset);
            await Assert.That(kept.IsActiveFor(Instant.FromUtc(2026, 11, 24, 13, 0), principal)).IsFalse();
        }
    }

    [Test]
    public async Task A_session_of_another_principal_and_an_already_revoked_one_are_left_alone()
    {
        // Arrange
        var (principal, kept, other) = Seed();
        var earlier = Instant.FromUtc(2026, 11, 24, 12, 30);
        other.Revoke(RevocationReason.RevokedByHolder, earlier);
        other.DequeueDomainEvents();
        var stranger = ActivePrincipal();
        _sessions.Principals.Add(stranger);
        var strangers = SessionOf(stranger, [3]);
        _sessions.Stored.Add(strangers);
        var strangerStamp = strangers.CredentialStamp;

        // Act
        await Change().ApplyAsync(principal, RevocationReason.PasswordChanged, kept.Id, default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(other.RevokedAt).IsEqualTo(earlier);
            await Assert.That(other.RevocationReason).IsEqualTo(RevocationReason.RevokedByHolder);
            await Assert.That(other.DomainEvents).IsEmpty();
            await Assert.That(strangers.RevokedAt).IsNull();
            await Assert.That(strangers.CredentialStamp).IsEqualTo(strangerStamp);
        }
    }

    [Test]
    public async Task A_session_to_keep_that_is_not_the_principals_is_not_restamped_and_everything_else_is_revoked()
    {
        // Arrange
        var (principal, kept, other) = Seed();

        // Act
        await Change().ApplyAsync(principal, RevocationReason.PasswordChanged, Guid.NewGuid(), default);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(kept.RevokedAt).IsNotNull();
            await Assert.That(other.RevokedAt).IsNotNull();
        }
    }
}
