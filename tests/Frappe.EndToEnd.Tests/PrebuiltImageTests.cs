namespace Frappe.EndToEnd.Tests;

/// <summary>
/// Naming the image a run deploys through <c>FRAPPE_E2E_IMAGE</c> (<see cref="PrebuiltImage"/>), against a fake environment lookup.
/// No Docker.
/// </summary>
public sealed class PrebuiltImageTests
{
    [Test]
    public async Task The_named_image_is_used_instead_of_a_build()
    {
        // Act
        var image = PrebuiltImage.From(name => name == PrebuiltImage.Variable ? " frappe-api:local " : null);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(image).IsNotNull();
            await Assert.That(image!.FullName).IsEqualTo("frappe-api:local");
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task No_image_is_named_when_the_variable_is_unset_or_blank(string? value)
    {
        // Act
        var image = PrebuiltImage.From(_ => value);

        // Assert
        await Assert.That(image).IsNull();
    }
}
