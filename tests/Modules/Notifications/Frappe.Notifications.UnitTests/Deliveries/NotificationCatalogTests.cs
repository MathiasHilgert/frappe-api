using Frappe.Notifications.Application.Kinds;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.UnitTests.Deliveries;

/// <summary>The catalog knows kinds by name and fails at startup on a repeated or malformed one (S0, design Pattern).</summary>
public sealed class NotificationCatalogTests
{
    [Test]
    public async Task A_kind_is_found_by_name_with_its_category_and_sample()
    {
        // Arrange
        var catalog = new NotificationCatalog([DispatchWorld.Security, DispatchWorld.Marketing]);

        // Act
        var found = catalog.TryGet("test_marketing", out var kind);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(found).IsTrue();
            await Assert.That(kind.Category).IsEqualTo(NotificationCategory.Marketing);
            await Assert.That(kind.Sample).IsEqualTo(TestMarketingNotification.Sample);
            await Assert.That(catalog.Kinds.Count).IsEqualTo(2);
        }
    }

    [Test]
    public async Task An_unknown_kind_is_not_found()
    {
        // Arrange
        var catalog = new NotificationCatalog([DispatchWorld.Security]);

        // Act
        var found = catalog.TryGet("nope", out _);

        // Assert
        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task A_repeated_kind_name_fails_the_catalog()
    {
        // Act
        var thrown = Assert.Throws<InvalidOperationException>(() => _ = new NotificationCatalog([DispatchWorld.Security, DispatchWorld.Security]));

        // Assert
        await Assert.That(thrown.Message).Contains("test_security");
    }

    [Test]
    public async Task A_kind_name_that_is_not_snake_case_fails_the_catalog()
    {
        // Act
        var thrown = Assert.Throws<InvalidOperationException>(() => _ = new NotificationCatalog([NotificationKind.Of<BadlyNamed, TestPayload>()]));

        // Assert
        await Assert.That(thrown.Message).Contains("AccountCode");
    }

    [Test]
    public async Task A_payload_of_the_wrong_type_is_a_bug_and_throws()
    {
        // Act
        var thrown = Assert.Throws<ArgumentException>(() => DispatchWorld.Security.RecipientOf("not a payload"));

        // Assert
        await Assert.That(thrown).IsNotNull();
    }

    /// <summary>A kind whose name breaks the snake case rule.</summary>
    private sealed class BadlyNamed : INotification<TestPayload>
    {
        public static string Kind => "AccountCode";

        public static NotificationCategory Category => NotificationCategory.Security;

        public static TestPayload Sample => new("ana@example.com", null);

        public Recipient RecipientOf(TestPayload payload) => Recipient.Reference("account", "1");
    }
}
