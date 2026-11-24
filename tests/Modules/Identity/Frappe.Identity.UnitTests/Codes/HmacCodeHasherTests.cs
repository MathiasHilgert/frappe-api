using System.Security.Cryptography;
using System.Text;
using Frappe.Identity.Infrastructure.Codes;
using Microsoft.Extensions.Options;

namespace Frappe.Identity.UnitTests.Codes;

/// <summary>Codes are hashed with HMAC-SHA256 under a configured key of at least 32 bytes, validated at start (S5, CFG-10).</summary>
public sealed class HmacCodeHasherTests
{
    [Test]
    public async Task A_code_hashes_to_the_hmac_sha256_of_its_text_under_the_key()
    {
        // Act
        var hash = CodeFixtures.Hasher().Hash("123456");

        // Assert
        var expected = HMACSHA256.HashData(Convert.FromBase64String(CodeFixtures.Key), Encoding.UTF8.GetBytes("123456"));
        await Assert.That(hash.ToArray()).IsEquivalentTo(expected);
    }

    [Test]
    public async Task A_candidate_matches_only_its_own_hash()
    {
        // Arrange
        var hasher = CodeFixtures.Hasher();
        var stored = hasher.Hash("123456");

        // Act
        var results = (hasher.Matches(stored, "123456"), hasher.Matches(stored, "123457"), hasher.Matches(stored, string.Empty));

        // Assert
        await Assert.That(results).IsEqualTo((true, false, false));
    }

    [Test]
    public async Task Another_key_makes_another_hash_of_the_same_code()
    {
        // Arrange
        var other = new HmacCodeHasher(Options.Create(new CodeHashOptions { Key = Convert.ToBase64String(new byte[32]) }));

        // Act
        var differs = other.Hash("123456") != CodeFixtures.Hasher().Hash("123456");

        // Assert
        await Assert.That(differs).IsTrue();
    }

    [Test]
    public async Task A_key_of_thirty_two_bytes_is_valid_and_one_byte_less_is_refused_naming_the_key()
    {
        // Act
        var valid = new CodeHashOptionsValidator().Validate(null, new CodeHashOptions { Key = Convert.ToBase64String(new byte[32]) });
        var short31 = new CodeHashOptionsValidator().Validate(null, new CodeHashOptions { Key = Convert.ToBase64String(new byte[31]) });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(valid.Succeeded).IsTrue();
            await Assert.That(short31.Failed).IsTrue();
            await Assert.That(short31.FailureMessage).Contains("Identity:Codes:Key").And.Contains("32 bytes");
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("not base64 at all!")]
    public async Task A_missing_or_malformed_key_is_refused_naming_the_key_and_the_hasher_will_not_build(string? key)
    {
        // Act
        var result = new CodeHashOptionsValidator().Validate(null, new CodeHashOptions { Key = key });
        var thrown = Assert.Throws<InvalidOperationException>(() => _ = new HmacCodeHasher(Options.Create(new CodeHashOptions { Key = key })));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.FailureMessage).Contains("Identity:Codes:Key");
            await Assert.That(thrown.Message).Contains("Identity:Codes:Key");
        }
    }
}
