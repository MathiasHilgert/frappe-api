using System.Net;
using System.Net.Sockets;
using Testcontainers.Nats;

namespace Frappe.TestUtilities;

/// <summary>
/// A NATS server with JetStream for a test assembly; each host takes a <see cref="NatsScope"/> of its own on it.
/// </summary>
public sealed class FrappeNats : IAsyncLifetime
{
    /// <summary>The server image, the one compose runs.</summary>
    public const string Image = "nats:2.14.9-alpine";

    private readonly NatsContainer _nats;

    /// <summary>Creates the server on a random host port.</summary>
    public FrappeNats()
        : this(fixedPort: false) { }

    // A fixed port survives a stop and a start, so a host keeps reaching the server after an outage.
    private FrappeNats(bool fixedPort)
    {
        NatsBuilder builder = new(Image);
        if (fixedPort)
        {
            Port = FreePort();
            builder = builder.WithPortBinding(Port, NatsBuilder.NatsClientPort);
        }

        _nats = builder.Build();
    }

    /// <summary>Gets the host port, known before the server starts only for <see cref="WithFixedPort"/>.</summary>
    public int Port { get; private set; }

    /// <summary>Gets the server URL.</summary>
    public string Url => $"nats://127.0.0.1:{Port}";

    /// <summary>Creates a server whose port stays the same across <see cref="StopAsync"/> and <see cref="StartAsync"/>.</summary>
    /// <returns>The server, not started.</returns>
    public static FrappeNats WithFixedPort() => new(fixedPort: true);

    /// <summary>Starts the server.</summary>
    /// <returns>A task that completes when the server answers.</returns>
    public async Task StartAsync()
    {
        await _nats.StartAsync(TestContext.Current.CancellationToken);
        Port = _nats.GetMappedPublicPort(NatsBuilder.NatsClientPort);
    }

    /// <summary>Stops the server; its JetStream data lives in the container and survives a restart.</summary>
    /// <returns>A task that completes when the server has stopped.</returns>
    public Task StopAsync() => _nats.StopAsync(TestContext.Current.CancellationToken);

    /// <inheritdoc/>
    public async ValueTask InitializeAsync() => await StartAsync();

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _nats.DisposeAsync();

    private static int FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
