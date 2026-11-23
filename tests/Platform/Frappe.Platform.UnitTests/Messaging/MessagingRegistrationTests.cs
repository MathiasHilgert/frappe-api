using Frappe.IntegrationEvents;
using Frappe.Messaging.Hosting;
using Frappe.Messaging.JetStream;
using Frappe.Messaging.Relay;
using Frappe.Messaging.Retention;
using Frappe.Platform.Testing.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.JetStream;

namespace Frappe.Platform.UnitTests.Messaging;

/// <summary>What a host registers for messaging: the purge, the JetStream connection, the relay and the consumers.</summary>
public sealed class MessagingRegistrationTests
{
    private static ServiceProvider Build(Action<IServiceCollection> register, params (string Key, string Value)[] settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build());
        register(services);
        return services.BuildServiceProvider();
    }

    /// <summary>The handler a consumer registration needs; it never runs here.</summary>
    private sealed class IdleHandler : IIntegrationEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>The context of a second producer module.</summary>
    private sealed class SecondContext(Microsoft.EntityFrameworkCore.DbContextOptions<SecondContext> options)
        : Microsoft.EntityFrameworkCore.DbContext(options);

    /// <summary>An integration event that forgot its type attribute.</summary>
    private sealed record Untyped : IIntegrationEvent;

    [Test]
    public async Task The_purge_of_a_module_reads_its_retention_from_the_configured_section()
    {
        // Arrange
        await using var provider = Build(
            services => services.AddMessagePurge<InboxTestContext>("test", "Custom:Retention"),
            ("Custom:Retention:OutboxDays", "3"),
            ("Custom:Retention:BatchSize", "25"));

        // Act
        var purger = provider.GetRequiredService<MessagePurger<InboxTestContext>>();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(purger.Module).IsEqualTo("test");
            await Assert.That(purger.Options.OutboxDays).IsEqualTo(3);
            await Assert.That(purger.Options.BatchSize).IsEqualTo(25);
            await Assert.That(purger.Options.InboxDays).IsEqualTo(14);
        }
    }

    [Test]
    public async Task A_retention_shorter_than_the_stream_keeps_is_rejected_naming_the_configured_key()
    {
        // Arrange
        await using var provider = Build(
            services => services.AddMessagePurge<InboxTestContext>("test", "Custom:Retention"),
            ("Custom:Retention:InboxDays", "1"));

        // Act
        var failure = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<MessagePurger<InboxTestContext>>());

        // Assert
        await Assert.That(failure.Message).Contains("Custom:Retention:InboxDays");
    }

    [Test]
    public async Task A_purge_needs_a_module_name()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var failure = Assert.Throws<ArgumentException>(() => services.AddMessagePurge<InboxTestContext>(" "));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("module");
    }

    [Test]
    public async Task The_jetstream_settings_given_in_code_name_the_streams_after_the_namespace()
    {
        // Arrange
        await using var provider = Build(services => services.AddJetStream("nats://localhost:4222", "tests", "ns1"));

        // Act
        var naming = provider.GetRequiredService<JetStreamNaming>();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(naming.Namespace).IsEqualTo("ns1");
            await Assert.That(provider.GetRequiredService<INatsJSContext>()).IsNotNull();
            await Assert.That(provider.GetRequiredService<IMessagePublisher>()).IsTypeOf<JetStreamPublisher>();
        }
    }

    [Test]
    public async Task Without_a_namespace_the_jetstream_names_are_the_production_names()
    {
        // Arrange
        await using var provider = Build(services => services.AddJetStream("nats://localhost:4222", "tests"));

        // Act
        var naming = provider.GetRequiredService<JetStreamNaming>();

        // Assert
        await Assert.That(naming).IsSameReferenceAs(JetStreamNaming.Default);
    }

    [Test]
    public async Task The_jetstream_url_and_namespace_come_from_the_connection_string_and_the_messaging_section()
    {
        // Arrange
        await using var provider = Build(
            services => services.AddJetStream("tests"),
            ("ConnectionStrings:Nats", "nats://broker:4222"),
            ("Messaging:Namespace", "ns2"));

        // Act
        var options = provider.GetRequiredService<IOptions<JetStreamOptions>>().Value;

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(options.Url).IsEqualTo("nats://broker:4222");
            await Assert.That(provider.GetRequiredService<JetStreamNaming>().Namespace).IsEqualTo("ns2");
        }
    }

    [Test]
    public async Task A_missing_nats_connection_string_fails_naming_the_key()
    {
        // Arrange
        await using var provider = Build(services => services.AddJetStream("tests"));

        // Act
        var failure = Assert.Throws<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<JetStreamOptions>>().Value);

        // Assert
        await Assert.That(failure.Message).Contains("ConnectionStrings:Nats");
    }

    [Test]
    public async Task A_jetstream_url_is_required_in_code()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var failure = Assert.Throws<ArgumentException>(() => services.AddJetStream(" ", "tests"));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("url");
    }

    [Test]
    public async Task A_consumer_registers_a_hosted_service_and_reads_its_backoff_from_the_configured_list()
    {
        // Arrange
        await using var provider = Build(
            services =>
            {
                services.AddJetStream("nats://localhost:4222", "tests");
                services.AddScoped<IIntegrationEventHandler<SomethingHappened>, IdleHandler>();
                services.AddIntegrationEventConsumer<InboxTestContext, SomethingHappened>("test", "test-reader", "Custom:Consumer");
            },
            ("Custom:Consumer:Backoff:0", "00:00:02"),
            ("Custom:Consumer:Backoff:1", "00:00:05"));

        // Act
        var hosted = provider.GetServices<IHostedService>().OfType<JetStreamConsumer<InboxTestContext, SomethingHappened>>().ToList();
        var options = provider.GetRequiredService<IOptionsMonitor<JetStreamConsumerOptions>>().Get("test-reader");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(hosted).Count().IsEqualTo(1);
            await Assert.That(options.Backoff).IsEquivalentTo([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)]);
        }
    }

    [Test]
    public async Task A_consumer_keeps_the_default_backoff_when_none_is_configured()
    {
        // Arrange
        await using var provider = Build(services =>
            services.AddIntegrationEventConsumer<InboxTestContext, SomethingHappened>("test", "test-reader"));

        // Act
        var options = provider.GetRequiredService<IOptionsMonitor<JetStreamConsumerOptions>>().Get("test-reader");

        // Assert
        await Assert.That(options.Backoff).IsEquivalentTo(new JetStreamConsumerOptions().Backoff);
    }

    [Test]
    public async Task An_event_without_a_type_attribute_cannot_be_consumed_and_fails_at_registration()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var failure = Assert.Throws<InvalidOperationException>(
            () => services.AddIntegrationEventConsumer<InboxTestContext, Untyped>("test", "test-reader"));

        // Assert
        await Assert.That(failure.Message).Contains("Untyped");
    }

    [Test]
    public async Task Each_producer_module_gets_its_own_relay_and_they_share_one_topology_service()
    {
        // Arrange
        await using var provider = Build(
            services =>
            {
                services.AddJetStream("nats://localhost:4222", "tests");
                services.AddOutboxRelay<InboxTestContext>("first");
                services.AddOutboxRelay<SecondContext>("second", "Custom:Relay");
            },
            ("Custom:Relay:MaxAttempts", "3"),
            ("Custom:Relay:RetryBackoff:0", "00:00:09"));

        // Act
        var hosted = provider.GetServices<IHostedService>().Select(service => service.GetType().Name).ToList();
        var modules = provider.GetServices<ProducerModule>().Select(module => module.Name).ToList();
        var second = provider.GetRequiredService<IOptionsMonitor<OutboxRelayOptions>>().Get("second");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(hosted.Count(name => name == "JetStreamTopologyService")).IsEqualTo(1);
            await Assert.That(hosted.Count(name => name.StartsWith("OutboxRelayService", StringComparison.Ordinal))).IsEqualTo(2);
            await Assert.That(modules).IsEquivalentTo(["first", "second"]);
            await Assert.That(second.MaxAttempts).IsEqualTo(3);
            await Assert.That(second.RetryBackoff).IsEquivalentTo([TimeSpan.FromSeconds(9)]);
        }
    }

    [Test]
    public async Task A_relay_needs_a_module_name()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var failure = Assert.Throws<ArgumentException>(() => services.AddOutboxRelay<InboxTestContext>(""));

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("module");
    }

    [Test]
    public async Task The_registered_relay_is_resolvable_with_its_dependencies()
    {
        // Arrange
        await using var provider = Build(services =>
        {
            services.AddJetStream("nats://localhost:4222", "tests");
            services.AddOutboxRelay<InboxTestContext>("first");
        });

        // Act
        var relay = provider.GetRequiredService<OutboxRelay<InboxTestContext>>();

        // Assert
        await Assert.That(relay).IsNotNull();
        await Assert.That(provider.GetRequiredService<ILoggerFactory>()).IsNotNull();
    }
}
