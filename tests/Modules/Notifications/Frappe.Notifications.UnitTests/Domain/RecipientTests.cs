using Frappe.Localization;
using Frappe.Notifications.Domain.Deliveries;

namespace Frappe.Notifications.UnitTests.Domain;

/// <summary>A recipient is a reference or a contact, keyed without ever holding an address in the clear (S3, S13).</summary>
public sealed class RecipientTests
{
    [Test]
    public async Task A_reference_is_keyed_by_kind_and_id()
    {
        // Act
        var recipient = (ReferenceRecipient)Recipient.Reference("account", "42");

        // Assert
        await Assert.That(RecipientKey.Of(recipient).Value).IsEqualTo("ref:account:42");
    }

    [Test]
    public async Task An_address_is_keyed_by_the_hash_key_id_and_the_lower_case_hex_of_the_hash()
    {
        // Arrange
        var hash = new AddressHash(Enumerable.Repeat((byte)0xAB, 32).ToArray(), 7);

        // Act
        var key = RecipientKey.Of(hash);

        // Assert
        await Assert.That(key.Value).IsEqualTo($"addr:7:{string.Concat(Enumerable.Repeat("ab", 32))}");
    }

    [Test]
    [Arguments("Account")]
    [Arguments("account1")]
    [Arguments("")]
    public async Task A_reference_with_a_kind_that_is_not_lower_snake_letters_is_a_bug_of_the_author(string kind)
    {
        // Act
        var thrown = Assert.Throws<ArgumentException>(() => Recipient.Reference(kind, "42"));

        // Assert
        await Assert.That(thrown).IsNotNull();
    }

    [Test]
    public async Task A_contact_masks_its_address_when_printed()
    {
        // Arrange
        var contact = Recipient.Contact(ChannelName.Create("mail").Value, "ana@example.com", LanguageTag.Create("es-AR").Value);

        // Act
        var text = contact.ToString();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(text).DoesNotContain("ana@example.com");
            await Assert.That(text).Contains("mail");
        }
    }

    [Test]
    public async Task An_address_hash_never_prints_its_bytes_and_compares_by_value_and_key()
    {
        // Arrange
        var hash = new AddressHash(new byte[32], 1);

        // Act
        var same = new AddressHash(new byte[32], 1).Equals(hash);
        var otherKey = new AddressHash(new byte[32], 2).Equals(hash);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(same).IsTrue();
            await Assert.That(otherKey).IsFalse();
            await Assert.That(hash.ToString()).DoesNotContain("00000000");
        }
    }

    [Test]
    [Arguments("mail", true)]
    [Arguments("in_app", true)]
    [Arguments("Email", false)]
    [Arguments("1sms", false)]
    [Arguments("", false)]
    [Arguments("a_channel_name_that_is_way_too_long_for_the_column", false)]
    public async Task A_channel_name_is_an_open_lower_snake_case_value_that_fits_its_column(string raw, bool valid)
    {
        // Act
        var result = ChannelName.Create(raw);

        // Assert
        await Assert.That(result.IsSuccess).IsEqualTo(valid);
    }
}
