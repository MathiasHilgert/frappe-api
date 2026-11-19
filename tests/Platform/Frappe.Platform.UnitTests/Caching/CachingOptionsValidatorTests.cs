using Frappe.Caching;

namespace Frappe.Platform.UnitTests.Caching;

/// <summary>The cache's settings are checked at startup, naming the full key (CFG-03).</summary>
public sealed class CachingOptionsValidatorTests
{
    [Test]
    public async Task A_missing_connection_string_fails_naming_its_full_key()
    {
        // Act
        var result = new CachingOptionsValidator().Validate(null, new CachingOptions { ConnectionString = " " });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.FailureMessage).IsEqualTo("Configuration 'ConnectionStrings:Valkey' is required.");
        }
    }

    [Test]
    public async Task A_connection_string_passes()
    {
        // Act
        var result = new CachingOptionsValidator().Validate(null, new CachingOptions { ConnectionString = "localhost:6379" });

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }
}
