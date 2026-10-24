using Frappe.Platform.Messaging;
using Npgsql;
using Shouldly;

namespace Frappe.IntegrationTests;

/// <summary>
/// Outside Development nothing creates Wolverine's message storage at startup: the deploy runs
/// <c>dotnet Frappe.Api.dll resources setup</c> first. That command runs in its own process, so these tests pin the
/// startup contract around it rather than the command itself.
/// </summary>
public sealed class HostTests(ApiFixture fixture)
{
    [Fact]
    public async Task Host_BootsAgainstPostgres_AndCreatesTheWolverineSchemaInDevelopment()
    {
        // The fixture host already booted in Development; it must have built Wolverine's message storage.
        (await SchemaExistsAsync(fixture.PostgresConnectionString)).ShouldBeTrue();
    }

    [Fact]
    public async Task Host_OutsideDevelopment_RefusesToStartWithoutTheWolverineSchema()
    {
        var connectionString = await fixture.CreateDatabaseAsync("production_without_schema");

        var exception = await Should.ThrowAsync<Exception>(() =>
            fixture.CreateHostAsync(environment: "Production", connectionString: connectionString));

        exception.ToString().ShouldContain("resources setup");
        (await SchemaExistsAsync(connectionString)).ShouldBeFalse();
    }

    [Fact]
    public async Task Host_OutsideDevelopment_StartsOnceTheWolverineSchemaExists()
    {
        var connectionString = await fixture.CreateDatabaseAsync("production_with_schema");
        await using (await fixture.CreateHostAsync(connectionString: connectionString))
        {
            // Development provisions the schema, standing in for the deploy's setup step.
        }

        (await SchemaExistsAsync(connectionString)).ShouldBeTrue();

        await using var host = await fixture.CreateHostAsync(environment: "Production", connectionString: connectionString);

        // Starting is not enough: the host must actually serve requests.
        await host.Scenario(s =>
        {
            s.Get.Url("/health/live");
            s.StatusCodeShouldBe(200);
        });
    }

    [Theory]
    [InlineData("Localization:DefaultLocale", "de", "DefaultLocale")]
    [InlineData("Platform:RateLimiting:PermitLimit", "0", "PermitLimit")]
    public async Task Host_RefusesToStart_WithAnInvalidPlatformOption(string key, string value, string member)
    {
        var error = await Should.ThrowAsync<Exception>(() => fixture.CreateHostAsync(configureSettings: s => s[key] = value));

        error.ToString().ShouldContain(member);
    }

    private static async Task<bool> SchemaExistsAsync(string connectionString)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = $1)");
        command.Parameters.Add(new NpgsqlParameter { Value = MessagingRegistration.Schema });
        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
