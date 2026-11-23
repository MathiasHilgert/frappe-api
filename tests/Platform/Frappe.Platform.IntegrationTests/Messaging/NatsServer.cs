using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Frappe.Messaging.JetStream;
using Frappe.Testing.Containers;
using NATS.Client.Core;
using NATS.Client.JetStream;
using TUnit.Core.Interfaces;

namespace Frappe.Platform.IntegrationTests.Messaging;

/// <summary>One real NATS server with JetStream per test session; every test isolates its streams with its own namespace.</summary>
public sealed class NatsServer : IAsyncInitializer, IAsyncDisposable
{
    private const int ClientPort = 4222;

    private readonly IContainer _container = new ContainerBuilder("nats:2.11-alpine")
        .WithCommand("--jetstream")
        .ReusedAs("nats")
        .WithPortBinding(ClientPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready"))
        .Build();

    private NatsConnection? _connection;

    /// <summary>The JetStream context of a connection shared by the session's tests.</summary>
    public INatsJSContext JetStream { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connection = new NatsConnection(new NatsOpts { Url = $"nats://{_container.Hostname}:{_container.GetMappedPublicPort(ClientPort)}" });
        await _connection.ConnectAsync();
        JetStream = new NatsJSContext(_connection);
    }

    /// <summary>Stream and subject names no other test uses.</summary>
    public static JetStreamNaming FreshNaming() => new("t" + Guid.NewGuid().ToString("N")[..12]);

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}
