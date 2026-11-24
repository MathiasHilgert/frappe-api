using Frappe.Identity.Domain.Sessions;
using Frappe.Identity.Infrastructure.Messaging;
using NodaTime;
using static Frappe.Identity.UnitTests.Sessions.SessionFixtures;
using Published = Frappe.Identity.Contracts.Sessions;

namespace Frappe.Identity.UnitTests.Sessions;

/// <summary>The facts a session raises are published under the catalog's names and spellings (S17).</summary>
public sealed class SessionEventsTests
{
    private static readonly IdentityIntegrationEvents Translator = new();

    [Test]
    public async Task A_session_that_opened_is_published_with_its_audience_spelled_as_stored()
    {
        // Arrange
        var principal = ActivePrincipal();
        var session = SessionOf(principal);

        // Act
        var published = session.DomainEvents.SelectMany(Translator.Translate).Single();

        // Assert
        await Assert.That(published).IsEqualTo(new Published.SessionOpened(principal.Id, session.Id, "console", Opened.ToDateTimeOffset()));
    }

    [Test]
    public async Task A_session_that_closed_is_published()
    {
        // Arrange
        var principal = ActivePrincipal();
        var session = SessionOf(principal);
        session.DequeueDomainEvents();
        var at = Opened + Duration.FromMinutes(5);
        session.Revoke(RevocationReason.Logout, at);

        // Act
        var published = session.DomainEvents.SelectMany(Translator.Translate).Single();

        // Assert
        await Assert.That(published).IsEqualTo(new Published.SessionClosed(principal.Id, session.Id, at.ToDateTimeOffset()));
    }

    [Test]
    public async Task A_session_that_was_revoked_is_published_with_its_reason_spelled_as_stored()
    {
        // Arrange
        var principal = ActivePrincipal();
        var session = SessionOf(principal);
        session.DequeueDomainEvents();
        var at = Opened + Duration.FromMinutes(5);
        session.Revoke(RevocationReason.OthersRevoked, at);

        // Act
        var published = session.DomainEvents.SelectMany(Translator.Translate).Single();

        // Assert
        await Assert.That(published).IsEqualTo(new Published.SessionRevoked(principal.Id, session.Id, "others_revoked", at.ToDateTimeOffset()));
    }

    [Test]
    public async Task A_password_that_changed_or_was_reset_is_published_as_its_own_fact()
    {
        // Arrange
        var principal = Guid.CreateVersion7();
        var at = Opened + Duration.FromMinutes(5);
        var changed = new Frappe.Identity.Domain.Credentials.PasswordChanged(principal, at);
        var reset = new Frappe.Identity.Domain.Credentials.PasswordReset(principal, at);

        // Act
        var published = new Frappe.Aggregates.IDomainEvent[] { changed, reset }.SelectMany(Translator.Translate).ToList();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(published[0]).IsEqualTo(new Frappe.Identity.Contracts.Credentials.PasswordChanged(principal, at.ToDateTimeOffset()));
            await Assert.That(published[1]).IsEqualTo(new Frappe.Identity.Contracts.Credentials.PasswordReset(principal, at.ToDateTimeOffset()));
        }
    }
}
