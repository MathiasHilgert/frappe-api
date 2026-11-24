using System.Buffers.Text;
using System.Security.Cryptography;
using Frappe.Identity.Infrastructure.Sessions;

namespace Frappe.Identity.UnitTests.Sessions;

/// <summary>
/// A session token is <c>fs_</c> and the base64url of 32 random bytes; only the SHA-256 of those bytes is kept, and a text is accepted only when
/// it is exactly what was issued (S8, SEC-07).
/// </summary>
public sealed class SessionTokensTests
{
    private static readonly SessionTokens Tokens = new();

    [Test]
    public async Task A_new_token_has_the_prefix_and_its_hash_is_the_sha256_of_its_random_bytes()
    {
        // Act
        var issued = Tokens.Issue();

        // Assert
        var secret = Base64Url.DecodeFromChars(issued.Value.AsSpan("fs_".Length));
        using (Assert.Multiple())
        {
            await Assert.That(issued.Value).StartsWith("fs_");
            await Assert.That(issued.Value.Length).IsEqualTo(46);
            await Assert.That(secret.Length).IsEqualTo(32);
            await Assert.That(issued.Hash.SequenceEqual(SHA256.HashData(secret))).IsTrue();
        }
    }

    [Test]
    public async Task Two_tokens_are_never_the_same()
    {
        // Act
        var first = Tokens.Issue();
        var second = Tokens.Issue();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first.Value).IsNotEqualTo(second.Value);
            await Assert.That(first.Hash.SequenceEqual(second.Hash)).IsFalse();
        }
    }

    [Test]
    public async Task A_presented_token_hashes_to_what_was_stored_when_it_was_issued()
    {
        // Arrange
        var issued = Tokens.Issue();

        // Act
        var hash = Tokens.HashOf(issued.Value);

        // Assert
        await Assert.That(hash!.SequenceEqual(issued.Hash)).IsTrue();
    }

    [Test]
    public async Task Every_token_that_is_issued_is_accepted_whatever_its_last_character()
    {
        // Act
        var refused = Enumerable.Range(0, 500).Select(_ => Tokens.Issue()).Count(issued => Tokens.HashOf(issued.Value) is null);

        // Assert
        await Assert.That(refused).IsEqualTo(0);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("fs_")]
    [Arguments("fs_short")]
    [Arguments("xx_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [Arguments("fs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [Arguments("fs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [Arguments("fs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    [Arguments("fs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/")]
    [Arguments("fs_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB")]
    [Arguments("FS_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task A_text_that_is_not_a_token_of_this_format_has_no_hash(string? presented)
    {
        // Act
        var hash = Tokens.HashOf(presented);

        // Assert
        await Assert.That(hash).IsNull();
    }

    [Test]
    public async Task A_token_masks_itself_when_printed()
    {
        // Act
        var issued = Tokens.Issue();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(issued.ToString()).DoesNotContain(issued.Value);
            await Assert.That(issued.ToString()).Contains("***");
        }
    }
}
