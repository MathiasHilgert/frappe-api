using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Credentials;
using Frappe.Results;

namespace Frappe.Identity.UnitTests.Credentials;

/// <summary>A password is valid by its length alone (S4): 10 to 128 characters, no composition rules, never trimmed.</summary>
public sealed class PasswordTests
{
    [Test]
    [Arguments(10)]
    [Arguments(64)]
    [Arguments(128)]
    public async Task A_password_from_ten_to_128_characters_is_accepted_as_typed(int length)
    {
        // Arrange
        var raw = new string('a', length);

        // Act
        var result = Password.Create(raw);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Value.Value).IsEqualTo(raw);
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("123456789")]
    public async Task A_shorter_password_is_too_short_and_the_error_names_the_minimum(string? raw)
    {
        // Act
        var result = Password.Create(raw);

        // Assert
        await AssertSingle(result, IdentityErrorCodes.PasswordTooShort, ("min", 10));
    }

    [Test]
    public async Task A_longer_password_is_too_long_and_the_error_names_the_maximum()
    {
        // Act
        var result = Password.Create(new string('a', 129));

        // Assert
        await AssertSingle(result, IdentityErrorCodes.PasswordTooLong, ("max", 128));
    }

    [Test]
    public async Task Spaces_count_and_are_never_trimmed_so_a_password_of_spaces_is_valid_and_kept()
    {
        // Arrange
        var raw = "  " + new string(' ', 8) + "  ";

        // Act
        var result = Password.Create(raw);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Value.Value).IsEqualTo(raw);
        }
    }

    [Test]
    public async Task Characters_are_counted_as_a_person_sees_them_so_an_emoji_counts_once()
    {
        // Arrange
        var nine = string.Concat(Enumerable.Repeat("\U0001F600", 9));
        var ten = string.Concat(Enumerable.Repeat("\U0001F600", 10));

        // Act
        var tooShort = Password.Create(nine);
        var enough = Password.Create(ten);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(tooShort.IsSuccess).IsFalse();
            await Assert.That(enough.IsSuccess).IsTrue();
        }
    }

    [Test]
    public async Task A_password_never_prints_its_text()
    {
        // Arrange
        var password = Password.Create("correct horse battery").Value;

        // Act
        var printed = $"{password} {password with { }}";

        // Assert
        await Assert.That(printed).IsEqualTo("*** ***");
    }

    [Test]
    public async Task A_hash_never_prints_its_text()
    {
        // Act
        var printed = new PasswordHash("$argon2id$v=19$m=19456,t=2,p=1$c2FsdA$aGFzaA").ToString();

        // Assert
        await Assert.That(printed).IsEqualTo("***");
    }

    [Test]
    public async Task A_credential_set_for_a_principal_keeps_the_hash_and_replace_swaps_it_and_its_time()
    {
        // Arrange
        var principal = Guid.CreateVersion7();
        var first = NodaTime.Instant.FromUtc(2026, 11, 24, 10, 0);
        var later = first.Plus(NodaTime.Duration.FromHours(1));
        var credential = PasswordCredential.Set(principal, new PasswordHash("hash-one"), first);

        // Act
        var before = (credential.Hash, credential.ChangedAt);
        credential.Replace(new PasswordHash("hash-two"), later);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(credential.PrincipalId).IsEqualTo(principal);
            await Assert.That(before).IsEqualTo(("hash-one", first));
            await Assert.That((credential.Hash, credential.ChangedAt)).IsEqualTo(("hash-two", later));
        }
    }

    private static async Task AssertSingle(Result<Password> result, string code, (string Name, object? Value) parameter)
    {
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Errors.Count).IsEqualTo(1);
            await Assert.That(result.Errors[0]).IsTypeOf<RuleViolationError>();
            await Assert.That(result.Errors[0].Code).IsEqualTo(code);
            await Assert.That(result.Errors[0].Parameters.Select(p => (p.Key, p.Value))).IsEquivalentTo([parameter]);
        }
    }
}
