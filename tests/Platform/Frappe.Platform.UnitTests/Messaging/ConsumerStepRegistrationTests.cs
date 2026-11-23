using Frappe.IntegrationEvents;
using Frappe.Messaging.Hosting;
using Frappe.Messaging.Inbox;
using Frappe.Platform.Testing.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.UnitTests.Messaging;

/// <summary>
/// A consumer's steps that would silently never run, replace one another or run twice fail the host instead: at
/// registration when the mistake is already visible there, otherwise when the host starts.
/// </summary>
public sealed class ConsumerStepRegistrationTests
{
    private static HostApplicationBuilder Builder() =>
        Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });

    [Test]
    public async Task A_step_for_a_consumer_that_does_not_exist_fails_the_host_at_startup()
    {
        // Arrange
        var builder = Builder();
        builder.Services.AddAfterCommitHandler<SomethingHappened, HookA>("b-cache");
        using var host = builder.Build();

        // Act
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure!.Message).Contains("b-cache");
            await Assert.That(failure.Message).Contains(nameof(SomethingHappened));
            await Assert.That(failure.Message).Contains(nameof(HookA));
        }
    }

    [Test]
    public async Task A_second_handler_for_the_same_consumer_fails_instead_of_replacing_the_first()
    {
        // Arrange
        var services = new ServiceCollection().AddIntegrationEventHandler<SomethingHappened, HandlerA>("a-projection");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() =>
            services.AddIntegrationEventHandler<SomethingHappened, HandlerB>("a-projection"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure.Message).Contains("a-projection");
            await Assert.That(failure.Message).Contains(nameof(SomethingHappened));
            await Assert.That(failure.Message).Contains(nameof(HandlerA));
            await Assert.That(failure.Message).Contains(nameof(HandlerB));
        }
    }

    [Test]
    public async Task The_same_after_commit_step_registered_twice_for_one_consumer_fails_instead_of_running_twice()
    {
        // Arrange
        var services = new ServiceCollection().AddAfterCommitHandler<SomethingHappened, HookA>("b-cache");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() =>
            services.AddAfterCommitHandler<SomethingHappened, HookA>("b-cache"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure.Message).Contains("b-cache");
            await Assert.That(failure.Message).Contains(nameof(SomethingHappened));
            await Assert.That(failure.Message).Contains(nameof(HookA));
        }
    }

    [Test]
    public async Task A_second_before_claim_step_for_the_same_consumer_fails_instead_of_replacing_the_first()
    {
        // Arrange
        var services = new ServiceCollection().AddBeforeClaimHandler<SomethingHappened, EarlyA>("a-mailer");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() =>
            services.AddBeforeClaimHandler<SomethingHappened, EarlyB>("a-mailer"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure.Message).Contains("a-mailer");
            await Assert.That(failure.Message).Contains(nameof(SomethingHappened));
            await Assert.That(failure.Message).Contains(nameof(EarlyA));
            await Assert.That(failure.Message).Contains(nameof(EarlyB));
        }
    }

    [Test]
    public async Task Steps_of_a_registered_consumer_start_whatever_the_registration_order()
    {
        // Arrange
        var builder = Builder();
        builder.Services
            .AddAfterCommitHandler<SomethingHappened, HookA>("a-projection")
            .AddIntegrationEventConsumer<InboxTestContext, SomethingHappened>("test", "a-projection")
            .AddIntegrationEventHandler<SomethingHappened, HandlerA>("a-projection")
            .AddAfterCommitHandler<SomethingHappened, HookB>("a-projection")
            .AddScoped<IIntegrationEventHandler<SomethingHappened>, HandlerB>();

        // Only the startup validation is under test; the consumer itself would need NATS.
        foreach (var hosted in builder.Services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
        {
            builder.Services.Remove(hosted);
        }

        using var host = builder.Build();

        // Act
        var failure = await Caught.ExceptionOfAsync(async () =>
        {
            await host.StartAsync();
            await host.StopAsync();
        });

        // Assert
        await Assert.That(failure).IsNull();
    }

    /// <summary>A handler of the sample event, first of two.</summary>
    private sealed class HandlerA : IIntegrationEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A handler of the sample event, second of two.</summary>
    private sealed class HandlerB : IIntegrationEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>An after-commit handler of the sample event, first of two.</summary>
    private sealed class HookA : IAfterCommitHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>An after-commit handler of the sample event, second of two.</summary>
    private sealed class HookB : IAfterCommitHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A before-claim step of the sample event, first of two.</summary>
    private sealed class EarlyA : IBeforeClaimHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A before-claim step of the sample event, second of two.</summary>
    private sealed class EarlyB : IBeforeClaimHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
