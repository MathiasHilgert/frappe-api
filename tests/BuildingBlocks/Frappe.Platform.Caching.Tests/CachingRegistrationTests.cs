using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Frappe.Platform.Caching.Tests;

public sealed class CachingRegistrationTests
{
    private static readonly IConfiguration NoConnectionStrings = new ConfigurationBuilder().Build();

    [Fact]
    public void AddCaching_MissingConnectionStringOutsideDevelopment_StillRegisters()
    {
        // Commands such as `resources setup` compose the host without starting it, so registration must not throw.
        var services = new ServiceCollection().AddCaching(NoConnectionStrings, EnvironmentNamed(Environments.Production));

        services.ShouldContain(d => d.ServiceType == typeof(ValkeyConnection));
    }

    [Fact]
    public void Startup_MissingConnectionStringOutsideDevelopment_FailsFast()
    {
        using var provider = new ServiceCollection()
            .AddCaching(NoConnectionStrings, EnvironmentNamed(Environments.Production))
            .BuildServiceProvider();

        var error = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        error.Message.ShouldContain("Connection string 'valkey' is missing.");
    }

    [Fact]
    public void Startup_MissingConnectionStringInDevelopment_FallsBackToLocalhost()
    {
        using var provider = new ServiceCollection()
            .AddCaching(NoConnectionStrings, EnvironmentNamed(Environments.Development))
            .BuildServiceProvider();

        Should.NotThrow(() => provider.GetRequiredService<IStartupValidator>().Validate());
        provider.GetRequiredService<IOptions<ValkeyOptions>>().Value.ConnectionString.ShouldBe("localhost:6379");
    }

    private static HostingEnvironment EnvironmentNamed(string name) => new() { EnvironmentName = name };
}
