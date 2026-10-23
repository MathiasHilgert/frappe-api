using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Shouldly;

namespace Frappe.Platform.Caching.Tests;

public sealed class CachingRegistrationTests
{
    private static readonly IConfiguration NoConnectionStrings = new ConfigurationBuilder().Build();

    [Fact]
    public void AddCaching_MissingConnectionStringOutsideDevelopment_FailsFast()
    {
        var error = Should.Throw<InvalidOperationException>(() =>
            new ServiceCollection().AddCaching(NoConnectionStrings, EnvironmentNamed(Environments.Production)));

        error.Message.ShouldBe("Connection string 'valkey' is missing.");
    }

    [Fact]
    public void AddCaching_MissingConnectionStringInDevelopment_FallsBackToLocalhost()
    {
        var services = new ServiceCollection().AddCaching(NoConnectionStrings, EnvironmentNamed(Environments.Development));

        services.ShouldContain(d => d.ServiceType == typeof(ValkeyConnection));
    }

    private static HostingEnvironment EnvironmentNamed(string name) => new() { EnvironmentName = name };
}
