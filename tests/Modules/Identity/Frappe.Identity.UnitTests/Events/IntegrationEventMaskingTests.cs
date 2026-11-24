using Frappe.Identity.Contracts.Owners;

namespace Frappe.Identity.UnitTests.Events;

/// <summary>An event that holds an address, a name or a code prints none of them (EVT-20), so a log line cannot leak them.</summary>
public sealed class IntegrationEventMaskingTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task OwnerCodeIssued_prints_neither_the_code_nor_the_address_nor_the_name()
    {
        // Arrange
        var issued = new OwnerCodeIssued(Guid.CreateVersion7(), "email_verification", "ana@example.com", "Ana", "es-AR", "482913", At);

        // Act
        var text = issued.ToString();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(text).DoesNotContain("482913").And.DoesNotContain("ana@example.com").And.DoesNotContain("Ana,");
            await Assert.That(text).Contains(issued.OwnerId.ToString()).And.Contains("email_verification").And.Contains("es-AR");
        }
    }

    [Test]
    public async Task OwnerRegistrationAttempted_prints_no_address()
    {
        // Arrange
        var attempted = new OwnerRegistrationAttempted("ana@example.com", "es-AR", At);

        // Act
        var text = attempted.ToString();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(text).DoesNotContain("ana@example.com");
            await Assert.That(text).Contains("es-AR");
        }
    }

    [Test]
    public async Task OwnerEmailChangeRequested_prints_neither_address_nor_name()
    {
        // Arrange
        var requested = new OwnerEmailChangeRequested(Guid.CreateVersion7(), "old@example.com", "n***@example.com", "pt-BR", "Ana", At);

        // Act
        var text = requested.ToString();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(text).DoesNotContain("old@example.com").And.DoesNotContain("n***@example.com").And.DoesNotContain("Name = Ana");
            await Assert.That(text).Contains(requested.OwnerId.ToString()).And.Contains("pt-BR");
        }
    }
}
