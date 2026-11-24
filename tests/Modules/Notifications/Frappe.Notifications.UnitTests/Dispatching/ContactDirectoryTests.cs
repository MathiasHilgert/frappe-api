using Frappe.Notifications.Application.Contacts;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.UnitTests.Dispatching;

/// <summary>The directory sends each reference to the resolver of its kind; with none registered nobody resolves (S3).</summary>
public sealed class ContactDirectoryTests
{
    private static readonly ReferenceRecipient Account = new("account", "42");

    [Test]
    public async Task With_no_resolver_registered_every_reference_resolves_to_nobody()
    {
        // Arrange
        var directory = new ContactDirectory([]);

        // Act
        var contacts = await directory.ResolveAsync(Account, CancellationToken.None);

        // Assert
        await Assert.That(contacts).IsEmpty();
    }

    [Test]
    public async Task A_reference_goes_to_the_resolver_of_its_kind_only()
    {
        // Arrange
        var mail = DispatchWorld.Contact(TestChannels.Mail, "ana@example.com");
        var directory = new ContactDirectory(
        [
            new FakeResolver("customer", new() { ["42"] = [DispatchWorld.Contact(TestChannels.Text, "+54")] }),
            new FakeResolver("account", new() { ["42"] = [mail] }),
        ]);

        // Act
        var contacts = await directory.ResolveAsync(Account, CancellationToken.None);

        // Assert
        await Assert.That(contacts).IsEquivalentTo([mail]);
    }
}
