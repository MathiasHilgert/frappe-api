using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Identifiers;
using Frappe.Results;

namespace Frappe.Identity.UnitTests.Identifiers;

/// <summary>An email address is trimmed, bounded and shaped like an address; its normalized form is what uniqueness compares (S3, DOM-05).</summary>
public sealed class EmailTests
{
    [Test]
    public async Task An_address_is_kept_as_typed_but_trimmed_and_normalized_to_lower_case()
    {
        // Act
        var result = Email.Create("  Ana.Perez@Example.COM ");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Value.Address).IsEqualTo("Ana.Perez@Example.COM");
            await Assert.That(result.Value.NormalizedAddress).IsEqualTo("ana.perez@example.com");
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("ana")]
    [Arguments("ana@")]
    [Arguments("@example.com")]
    [Arguments("ana@example")]
    [Arguments("ana@@example.com")]
    [Arguments("a na@example.com")]
    [Arguments("ana@exa mple.com")]
    [Arguments("ana@example..com")]
    [Arguments("ana@.example.com")]
    public async Task Text_that_is_not_an_address_is_refused_with_the_one_code(string? raw)
    {
        // Act
        var result = Email.Create(raw);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Errors.Single()).IsEqualTo(Error.InvalidValue(IdentityErrorCodes.EmailInvalid));
        }
    }

    [Test]
    public async Task An_address_longer_than_254_characters_is_refused()
    {
        // Act
        var result = Email.Create(new string('a', 250) + "@example.com");

        // Assert
        await Assert.That(result.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.EmailInvalid);
    }

    [Test]
    public async Task An_address_prints_only_its_domain()
    {
        // Act
        var text = Email.Create("ana@example.com").Value.ToString();

        // Assert
        await Assert.That(text).IsEqualTo("***@example.com");
    }
}
