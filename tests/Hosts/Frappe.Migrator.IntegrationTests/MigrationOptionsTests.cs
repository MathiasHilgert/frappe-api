namespace Frappe.Migrator.IntegrationTests;

/// <summary>The Migrator's options never print the secrets they hold.</summary>
public sealed class MigrationOptionsTests
{
    [Test]
    public async Task A_role_prints_without_its_password()
    {
        // Arrange
        var role = new Settings.RoleOptions { Password = "s3cret-role" };

        // Act
        var text = role.ToString();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(text).DoesNotContain("s3cret-role");
            await Assert.That(text).Contains("***");
        }
    }

    [Test]
    public async Task The_options_print_the_owner_connection_string_without_its_password_and_the_role_names_only()
    {
        // Arrange
        var options = new Settings.MigrationOptions { OwnerConnectionString = "Host=db;Username=owner;Password=s3cret-owner" };
        options.Roles["scheduler"] = new Settings.RoleOptions { Password = "s3cret-role" };

        // Act
        var text = options.ToString();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(text).DoesNotContain("s3cret-owner");
            await Assert.That(text).DoesNotContain("s3cret-role");
            await Assert.That(text).Contains("Host=db");
            await Assert.That(text).Contains("scheduler");
        }
    }
}
