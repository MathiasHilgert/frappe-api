using Frappe.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.UnitTests.Caching;

/// <summary>The cache reads its settings from configuration and falls back to the default instance name (CFG-03).</summary>
public sealed class CachingRegistrationTests
{
    [Test]
    public async Task Settings_given_in_code_must_not_be_blank()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var blankConnection = Assert.Throws<ArgumentException>(() => services.AddFrappeCaching(" ", "frappe"));
        var blankInstance = Assert.Throws<ArgumentException>(() => services.AddFrappeCaching("localhost:6379", " "));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(blankConnection.ParamName).IsEqualTo("valkeyConnectionString");
            await Assert.That(blankInstance.ParamName).IsEqualTo("instanceName");
            await Assert.That(services).IsEmpty();
        }
    }

    [Test]
    public async Task Settings_given_in_code_are_the_cache_options()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddFrappeCaching("localhost:6379", "staging");

        // Assert
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CachingOptions>>().Value;
        using (Assert.Multiple())
        {
            await Assert.That(options.ConnectionString).IsEqualTo("localhost:6379");
            await Assert.That(options.InstanceName).IsEqualTo("staging");
        }
    }

    [Test]
    public async Task An_empty_instance_name_falls_back_to_the_default()
    {
        // Arrange
        await using var provider = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Valkey"] = "localhost:6379",
            ["Caching:InstanceName"] = " ",
        });

        // Act
        var options = provider.GetRequiredService<IOptions<CachingOptions>>().Value;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(options.InstanceName).IsEqualTo("frappe");
            await Assert.That(options.ConnectionString).IsEqualTo("localhost:6379");
        }
    }

    [Test]
    public async Task A_missing_connection_string_fails_naming_its_full_key()
    {
        // Arrange
        await using var provider = Build([]);

        // Act
        var exception = Assert.Throws<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<CachingOptions>>().Value);

        // Assert
        await Assert.That(exception.Message).Contains("ConnectionStrings:Valkey");
    }

    [Test]
    public async Task The_window_counter_is_registered_as_one_shared_instance()
    {
        // Arrange
        await using var provider = Build(new Dictionary<string, string?> { ["ConnectionStrings:Valkey"] = "localhost:6379" });

        // Act
        var first = provider.GetRequiredService<ValkeyWindowCounter>();
        var second = provider.GetRequiredService<ValkeyWindowCounter>();

        // Assert
        await Assert.That(second).IsSameReferenceAs(first);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddFrappeCaching();
        return services.BuildServiceProvider();
    }
}
