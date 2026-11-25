using Frappe.Notifications.Infrastructure.Contacts;
using Frappe.Notifications.UnitTests.Deliveries;

namespace Frappe.Notifications.UnitTests.Contacts;

/// <summary>Addresses are stored only as keyed hashes of their normalized form, and keys rotate (S13).</summary>
public sealed class AddressHasherTests
{
    private static readonly string OtherKey = Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray());

    [Test]
    public async Task Spellings_of_one_address_hash_alike_and_different_addresses_do_not()
    {
        // Arrange
        var hasher = TestHasher.Create();

        // Act
        var a = hasher.Hash("Ana@Example.com ");
        var b = hasher.Hash("ana@example.com");
        var other = hasher.Hash("bea@example.com");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(a).IsEqualTo(b);
            await Assert.That(a).IsNotEqualTo(other);
            await Assert.That(a.KeyId).IsEqualTo((short)1);
        }
    }

    [Test]
    public async Task Composed_and_decomposed_unicode_hash_alike()
    {
        // Arrange
        var hasher = TestHasher.Create();

        // Act
        var composed = hasher.Hash("josé@example.com");
        var decomposed = hasher.Hash("josé@example.com");

        // Assert
        await Assert.That(composed).IsEqualTo(decomposed);
    }

    [Test]
    public async Task After_a_rotation_new_hashes_use_the_current_key_and_a_lookup_probes_every_key()
    {
        // Arrange
        var before = TestHasher.Create();
        var rotated = TestHasher.Create(2, (2, OtherKey));

        // Act
        var all = rotated.HashWithEveryKey("ana@example.com");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(rotated.Hash("ana@example.com").KeyId).IsEqualTo((short)2);
            await Assert.That(all.Select(h => h.KeyId).Order()).IsEquivalentTo(new short[] { 1, 2 });
            await Assert.That(all).Contains(before.Hash("ana@example.com"));
            await Assert.That(all.Distinct().Count()).IsEqualTo(2);
        }
    }

    [Test]
    public async Task A_complete_configuration_is_valid()
    {
        // Arrange
        var options = new AddressHashOptions { CurrentKeyId = 1 };
        options.Keys[1] = OtherKey;

        // Act
        var result = new AddressHashOptionsValidator().Validate(null, options);

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task A_missing_current_key_a_short_key_and_a_non_base64_key_each_fail_startup_naming_the_key()
    {
        // Arrange
        var options = new AddressHashOptions { CurrentKeyId = 9 };
        options.Keys[1] = Convert.ToBase64String(new byte[8]);
        options.Keys[2] = "not base64!";
        options.Keys[0] = OtherKey;

        // Act
        var result = new AddressHashOptionsValidator().Validate(null, options);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.Failures!)
                .Contains("Notifications:AddressHash:CurrentKeyId must name one of the keys in Notifications:AddressHash:Keys.");
            await Assert.That(result.Failures!.Count(f => f.Contains("base64 of at least 32 bytes"))).IsEqualTo(2);
            await Assert.That(result.Failures!).Contains("Notifications:AddressHash:Keys:0 must have a positive id.");
        }
    }

    [Test]
    public async Task A_key_longer_than_sixty_four_bytes_is_valid()
    {
        // Arrange
        var options = new AddressHashOptions { CurrentKeyId = 1 };
        options.Keys[1] = Convert.ToBase64String(Enumerable.Repeat((byte)5, 96).ToArray());

        // Act
        var result = new AddressHashOptionsValidator().Validate(null, options);

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task No_configured_keys_fails_startup()
    {
        // Act
        var result = new AddressHashOptionsValidator().Validate(null, new AddressHashOptions());

        // Assert
        await Assert.That(result.Failed).IsTrue();
    }
}
