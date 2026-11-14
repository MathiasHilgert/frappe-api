using Frappe.Testing;
using Microsoft.Extensions.Configuration;

namespace Frappe.Geo.IntegrationTests.Hosts;

/// <summary>What the hosts write to their logs in production: no SQL by default.</summary>
public sealed class HostLoggingTests
{
    [Test]
    [Arguments("src/Hosts/Frappe.Api")]
    [Arguments("src/Hosts/Frappe.Worker")]
    public async Task A_host_logs_SQL_commands_in_development_only(string host)
    {
        var production = Settings(host, "appsettings.json");
        var development = Settings(host, "appsettings.json", "appsettings.Development.json");

        await Assert.That(production["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"]).IsEqualTo("Warning");
        await Assert.That(development["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"]).IsEqualTo("Information");
    }

    /// <summary>A host's settings files layered as the host loads them, later ones winning.</summary>
    private static IConfigurationRoot Settings(string host, params string[] files)
    {
        var builder = new ConfigurationBuilder();
        foreach (var file in files)
        {
            builder.AddJsonFile(Path.Combine(RepositoryRoot.FullPath, host, file), optional: false);
        }

        return builder.Build();
    }
}
