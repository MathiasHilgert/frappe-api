using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Owners;

namespace Frappe.Identity.UnitTests.Owners;

/// <summary>
/// An owner's name is what a mail greets them with: at most 60 characters given plus family, and nothing that looks like a link (S3).
/// </summary>
public sealed class OwnerNameTests
{
    [Test]
    public async Task A_given_and_a_family_name_are_trimmed_and_mailed_together()
    {
        // Act
        var result = OwnerName.Create("  Ana ", " Pérez ");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Value.Given).IsEqualTo("Ana");
            await Assert.That(result.Value.Family).IsEqualTo("Pérez");
            await Assert.That(result.Value.Mailed).IsEqualTo("Ana Pérez");
        }
    }

    [Test]
    public async Task A_missing_family_name_stays_empty()
    {
        // Act
        var result = OwnerName.Create("Ana", "  ");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Value.Family).IsNull();
            await Assert.That(result.Value.Mailed).IsEqualTo("Ana");
        }
    }

    [Test]
    [Arguments(null, null)]
    [Arguments("", "Pérez")]
    [Arguments("   ", null)]
    [Arguments("visit http://x.io", null)]
    [Arguments("Ana", "WWW.example.com")]
    [Arguments("Ana", "a://b")]
    public async Task A_name_without_a_given_part_or_with_a_link_is_refused(string? given, string? family)
    {
        // Act
        var result = OwnerName.Create(given, family);

        // Assert
        await Assert.That(result.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.NameInvalid);
    }

    [Test]
    [Arguments("An\na", null)]
    [Arguments("Ana", "P\u0000rez")]
    [Arguments("Ana\u202Eevil", null)]
    [Arguments("Ana", "\u200Bx")]
    [Arguments("Ana\u2066", null)]
    [Arguments("\U000E0041na", null)]
    public async Task A_name_with_a_control_or_format_character_is_refused(string given, string? family)
    {
        // Act
        var result = OwnerName.Create(given, family);

        // Assert
        await Assert.That(result.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.NameInvalid);
    }

    [Test]
    public async Task The_given_name_plus_the_family_name_may_not_pass_60_characters()
    {
        // Act
        var fits = OwnerName.Create(new string('a', 30), new string('b', 29));
        var tooLong = OwnerName.Create(new string('a', 30), new string('b', 30));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(fits.IsSuccess).IsTrue();
            await Assert.That(tooLong.Errors.Single().Code).IsEqualTo(IdentityErrorCodes.NameInvalid);
        }
    }

    [Test]
    public async Task A_name_prints_nothing_of_itself()
    {
        // Act
        var text = OwnerName.Create("Ana", "Pérez").Value.ToString();

        // Assert
        await Assert.That(text).DoesNotContain("Ana").And.DoesNotContain("Pérez");
    }
}
