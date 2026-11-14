using Frappe.IntegrationEvents;
using Frappe.Messaging.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.Tests.Messaging;

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
        var builder = Builder();
        builder.Services.AddAfterCommitHandler<SomethingHappened, HookA>("b-cache");
        using var host = builder.Build();

        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        await Assert.That(failure!.Message).Contains("b-cache");
        await Assert.That(failure.Message).Contains(nameof(SomethingHappened));
        await Assert.That(failure.Message).Contains(nameof(HookA));
    }

    [Test]
    public async Task A_second_handler_for_the_same_consumer_fails_instead_of_replacing_the_first()
    {
        var services = new ServiceCollection().AddIntegrationEventHandler<SomethingHappened, HandlerA>("a-projection");

        var failure = Assert.Throws<InvalidOperationException>(() =>
            services.AddIntegrationEventHandler<SomethingHappened, HandlerB>("a-projection"));

        await Assert.That(failure.Message).Contains("a-projection");
        await Assert.That(failure.Message).Contains(nameof(SomethingHappened));
        await Assert.That(failure.Message).Contains(nameof(HandlerA));
        await Assert.That(failure.Message).Contains(nameof(HandlerB));
    }

    [Test]
    public async Task The_same_after_commit_step_registered_twice_for_one_consumer_fails_instead_of_running_twice()
    {
        var services = new ServiceCollection().AddAfterCommitHandler<SomethingHappened, HookA>("b-cache");

        var failure = Assert.Throws<InvalidOperationException>(() =>
            services.AddAfterCommitHandler<SomethingHappened, HookA>("b-cache"));

        await Assert.That(failure.Message).Contains("b-cache");
        await Assert.That(failure.Message).Contains(nameof(SomethingHappened));
        await Assert.That(failure.Message).Contains(nameof(HookA));
    }

    [Test]
    public async Task Steps_of_a_registered_consumer_start_whatever_the_registration_order()
    {
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
        await host.StartAsync();
        await host.StopAsync();
    }

    private sealed class HandlerA : IIntegrationEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class HandlerB : IIntegrationEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class HookA : IAfterCommitHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class HookB : IAfterCommitHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
