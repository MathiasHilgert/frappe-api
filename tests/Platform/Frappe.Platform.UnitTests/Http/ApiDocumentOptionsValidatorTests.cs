using Frappe.Http.OpenApi;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>The document's title and version are checked at startup, every failure naming its full key (CFG-03).</summary>
public sealed class ApiDocumentOptionsValidatorTests
{
    private readonly ApiDocumentOptionsValidator _validator = new();

    [Test]
    public async Task A_missing_title_and_a_missing_version_are_both_reported_naming_their_full_key()
    {
        // Act
        var result = _validator.Validate(null, new ApiDocumentOptions { Title = "", Version = " " });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.Failures!.Count()).IsEqualTo(2);
            await Assert.That(result.FailureMessage).Contains("Configuration 'Http:OpenApi:Title' is required.");
            await Assert.That(result.FailureMessage).Contains("Configuration 'Http:OpenApi:Version' is required.");
        }
    }

    [Test]
    public async Task The_defaults_pass_with_no_description()
    {
        // Act
        var result = _validator.Validate(null, new ApiDocumentOptions());

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }
}
