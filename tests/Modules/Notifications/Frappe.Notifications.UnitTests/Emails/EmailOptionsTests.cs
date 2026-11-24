using Frappe.Notifications.Channels.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>
/// The sender is chosen once, at composition, from <c>Notifications:Email:Sender</c>, and a deploy that picks Resend without its key fails
/// when the host starts, naming the key (DES-04, CFG-01, CFG-10).
/// </summary>
public sealed class EmailOptionsTests
{
    private static ServiceProvider Compose(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddEmailSender();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static async Task<string> StartFailureAsync(Dictionary<string, string?> settings)
    {
        await using var provider = Compose(settings);
        var thrown = await Assert.ThrowsAsync<OptionsValidationException>(
            () => provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(CancellationToken.None));
        return thrown!.Message;
    }

    [Test]
    public async Task Resend_is_the_default_and_a_deploy_without_its_key_fails_naming_it()
    {
        // Act
        var message = await StartFailureAsync([]);

        // Assert
        await Assert.That(message)
            .Contains("Configuration 'Notifications:Email:Resend:ApiKey' must be set when 'Notifications:Email:Sender' is Resend.");
    }

    [Test]
    public async Task An_unknown_sender_fails_the_start_naming_the_key()
    {
        // Arrange
        await using var provider = Compose(new() { ["Notifications:Email:Sender"] = "Smtp" });

        // Act
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(CancellationToken.None));

        // Assert
        await Assert.That(thrown!.Message).Contains("Notifications:Email:Sender");
    }

    [Test]
    public async Task A_request_rate_at_or_above_the_providers_limit_fails_naming_the_key()
    {
        // Act
        var message = await StartFailureAsync(new()
        {
            ["Notifications:Email:Resend:ApiKey"] = "k",
            ["Notifications:Email:Resend:RequestsPerSecond"] = "10",
        });

        // Assert
        await Assert.That(message).Contains("Configuration 'Notifications:Email:Resend:RequestsPerSecond' must be from 1 to 9");
    }

    [Test]
    public async Task A_total_budget_that_does_not_cover_its_attempts_fails_naming_the_key()
    {
        // Act
        var message = await StartFailureAsync(new()
        {
            ["Notifications:Email:Resend:ApiKey"] = "k",
            ["Notifications:Email:Resend:AttemptTimeout"] = "00:00:20",
            ["Notifications:Email:Resend:TotalTimeout"] = "00:00:10",
        });

        // Assert
        await Assert.That(message).Contains("Configuration 'Notifications:Email:Resend:TotalTimeout' must be longer than");
    }

    [Test]
    public async Task A_blank_from_address_fails_naming_the_key()
    {
        // Act
        var message = await StartFailureAsync(new() { ["Notifications:Email:Resend:ApiKey"] = "k", ["Notifications:Email:From"] = " " });

        // Assert
        await Assert.That(message).Contains("Configuration 'Notifications:Email:From' must be set.");
    }

    [Test]
    public async Task Resend_with_its_key_selects_the_resend_adapter_and_the_default_from_is_the_provider_sandbox_address()
    {
        // Arrange
        await using var provider = Compose(new() { ["Notifications:Email:Resend:ApiKey"] = "k" });

        // Act
        var sender = provider.GetRequiredService<IEmailSender>();
        var options = provider.GetRequiredService<IOptions<EmailOptions>>().Value;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(sender).IsTypeOf<ResendEmailSender>();
            await Assert.That(options.From).IsEqualTo("onboarding@resend.dev");
        }
    }

    [Test]
    public async Task Logging_needs_no_key_and_selects_the_logging_adapter()
    {
        // Arrange
        await using var provider = Compose(new() { ["Notifications:Email:Sender"] = "Logging" });

        // Act
        await provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(CancellationToken.None);

        // Assert
        await Assert.That(provider.GetRequiredService<IEmailSender>()).IsTypeOf<LoggingEmailSender>();
    }
}
