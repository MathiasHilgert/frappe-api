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
    [Arguments("user\n")]
    [Arguments("abcdefghijklmnopqrstuvwxyzabcdefghijklmno")]
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

    [Test]
    [Arguments("ref:account:42")]
    [Arguments("ref:order_line:a b/c")]
    [Arguments("ref:account:José Ñandú")]
    [Arguments("addr:12:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public async Task A_key_reads_back_from_its_stored_spelling(string text)
    {
        // Act
        var key = RecipientKey.Parse(text);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(key.IsSuccess).IsTrue();
            await Assert.That(key.Value.Value).IsEqualTo(text);
            await Assert.That(key.Value.IsAddress).IsEqualTo(text.StartsWith("addr:", StringComparison.Ordinal));
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("account:42")]
    [Arguments("ref:Account:42")]
    [Arguments("ref:account:")]
    [Arguments("ref:account:42\n")]
    [Arguments("ref:account:4\n2")]
    [Arguments("addr:1:ABCDEF")]
    [Arguments("addr:x:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [Arguments("addr:1:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0")]
    public async Task Text_that_is_not_a_stored_key_is_refused(string? text)
    {
        // Act
        var key = RecipientKey.Parse(text);

        // Assert
        await Assert.That(key.IsSuccess).IsFalse();
    }

    [Test]
    public async Task A_reference_kind_longer_than_the_cap_is_refused_and_the_longest_key_fits_its_column()
    {
        // Arrange
        var longest = new ReferenceRecipient(new string('a', ReferenceRecipient.MaxKindLength), new string('1', ReferenceRecipient.MaxIdLength));

        // Act
        var tooLong = Assert.Throws<ArgumentException>(() => Recipient.Reference(new string('a', ReferenceRecipient.MaxKindLength + 1), "1"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(tooLong).IsNotNull();
            await Assert.That(RecipientKey.Of(longest).Value.Length).IsLessThanOrEqualTo(RecipientKey.MaxLength);
            await Assert.That(RecipientKey.Parse(RecipientKey.Of(longest).Value).IsSuccess).IsTrue();
        }
    }

    [Test]
    public async Task A_reference_id_with_a_control_character_is_refused_as_the_key_would_not_read_back()
    {
        // Act
        var thrown = Assert.Throws<ArgumentException>(() => Recipient.Reference("account", "4\u00002"));

        // Assert
        await Assert.That(thrown).IsNotNull();
    }

    [Test]
    [Arguments("ref:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:1")]
    [Arguments("ref:account: ")]
    public async Task A_key_that_a_reference_could_not_build_is_refused(string text)
    {
        // Act
        var key = RecipientKey.Parse(text);

        // Assert
        await Assert.That(key.IsSuccess).IsFalse();
    }

    [Test]
    public async Task A_key_longer_than_the_columns_hold_is_refused()
    {
        // Act
        var key = RecipientKey.Parse("ref:account:" + new string('1', RecipientKey.MaxLength));

        // Assert
        await Assert.That(key.IsSuccess).IsFalse();
    }
}
