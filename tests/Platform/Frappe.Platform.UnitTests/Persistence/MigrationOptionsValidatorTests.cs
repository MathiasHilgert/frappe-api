using Frappe.Persistence;

namespace Frappe.Platform.UnitTests.Persistence;

/// <summary>A migration run refuses a missing owner connection string or role password before it touches the database, naming each key.</summary>
public sealed class MigrationOptionsValidatorTests
{
    private static readonly MigrationOptionsValidator Validator = new(["Orders", "Billing"]);

    [Test]
    public async Task Complete_options_are_accepted()
    {
        // Arrange
        var options = Complete();

        // Act
        var result = Validator.Validate(null, options);

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task A_missing_owner_connection_string_is_refused_naming_the_key()
    {
        // Arrange
        var options = Complete();
        options.OwnerConnectionString = " ";

        // Act
        var result = Validator.Validate(null, options);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failed).IsTrue();
            await Assert.That(result.Failures).Contains(f => f.Contains("ConnectionStrings:Owner", StringComparison.Ordinal));
        }
    }

    [Test]
    public async Task Every_role_without_a_password_is_refused_naming_its_key()
    {
        // Arrange
        var options = new MigrationOptions { OwnerConnectionString = "Host=localhost" };
        options.Roles["Orders"] = new ModuleRoleOptions { Password = null };

        // Act
        var result = Validator.Validate(null, options);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(result.Failures!.Count()).IsEqualTo(2);
            await Assert.That(result.Failures).Contains(f => f.Contains("Roles:Orders:Password", StringComparison.Ordinal));
            await Assert.That(result.Failures).Contains(f => f.Contains("Roles:Billing:Password", StringComparison.Ordinal));
        }
    }

    [Test]
    public async Task Role_names_match_without_regard_to_case()
    {
        // Arrange
        var options = new MigrationOptions { OwnerConnectionString = "Host=localhost" };
        options.Roles["orders"] = new ModuleRoleOptions { Password = "p" };
        options.Roles["BILLING"] = new ModuleRoleOptions { Password = "p" };

        // Act
        var result = Validator.Validate(null, options);

        // Assert
        await Assert.That(result.Succeeded).IsTrue();
    }

    private static MigrationOptions Complete()
    {
        var options = new MigrationOptions { OwnerConnectionString = "Host=localhost" };
        options.Roles["Orders"] = new ModuleRoleOptions { Password = "orders-password" };
        options.Roles["Billing"] = new ModuleRoleOptions { Password = "billing-password" };
        return options;
    }
}
