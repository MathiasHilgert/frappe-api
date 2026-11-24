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
}
