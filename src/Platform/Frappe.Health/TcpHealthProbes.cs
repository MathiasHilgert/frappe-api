using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frappe.Health;

/// <summary>The ports a process without HTTP opens for Kubernetes <c>tcpSocket</c> probes.</summary>
public sealed record TcpProbePorts(int Live, int Ready, int Startup)
{
    public static TcpProbePorts Default { get; } = new(8081, 8082, 8083);
}

/// <summary>
/// Health for a process that serves no HTTP (the Worker), the model of Microsoft's Kubernetes health check
/// publisher: every few seconds the checks run, and each probe's port is open only while that probe is healthy.
/// A closed port is what a <c>tcpSocket</c> probe reads as a failure.
/// </summary>
public static class TcpHealthProbes
{
    /// <summary>
    /// The probes on the ports of <c>Health:Probes</c> (<see cref="TcpProbeOptions"/>); a port outside 1 to 65535
    /// fails at startup naming the key.
    /// </summary>
    public static IServiceCollection AddTcpHealthProbes(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var probes = services.AddOptions<TcpProbeOptions>().BindConfiguration(TcpProbeOptions.Section);
        foreach (var (key, port) in new (string Key, Func<TcpProbeOptions, int> Port)[]
        {
            (nameof(TcpProbeOptions.LivePort), options => options.LivePort),
            (nameof(TcpProbeOptions.ReadyPort), options => options.ReadyPort),
            (nameof(TcpProbeOptions.StartupPort), options => options.StartupPort),
        })
        {
            probes.Validate(options => port(options) is >= 1 and <= 65535, $"Configuration '{TcpProbeOptions.Section}:{key}' must be a port from 1 to 65535.");
        }

        probes.ValidateOnStart();
        return services.AddTcpHealthProbeServices();
    }

    /// <summary>The same as <see cref="AddTcpHealthProbes(IServiceCollection)"/>, with the ports given in code.</summary>
    public static IServiceCollection AddTcpHealthProbes(this IServiceCollection services, TcpProbePorts ports)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(ports);
        services.AddOptions<TcpProbeOptions>().Configure(options =>
        {
            options.LivePort = ports.Live;
            options.ReadyPort = ports.Ready;
            options.StartupPort = ports.Startup;
        });
        return services.AddTcpHealthProbeServices();
    }

    private static IServiceCollection AddTcpHealthProbeServices(this IServiceCollection services)
    {
        services.AddFrappeHealth();
        services.Configure<HealthCheckPublisherOptions>(options =>
        {
            options.Delay = TimeSpan.FromSeconds(1);
            options.Period = TimeSpan.FromSeconds(5);
            options.Timeout = HealthRegistration.CheckTimeout * 2;
        });
        services.AddSingleton(provider => Ports(provider.GetRequiredService<IOptions<TcpProbeOptions>>().Value));
        services.AddSingleton<IHealthCheckPublisher, TcpProbePublisher>();
        return services;
    }

    private static TcpProbePorts Ports(TcpProbeOptions options) => new(options.LivePort, options.ReadyPort, options.StartupPort);
}

internal sealed partial class TcpProbePublisher(TcpProbePorts ports, ILogger<TcpProbePublisher> logger) : IHealthCheckPublisher, IAsyncDisposable
{
    private readonly ProbeListener[] _probes =
    [
        new(HealthProbes.Live, ports.Live),
        new(HealthProbes.Ready, ports.Ready),
        new(HealthProbes.Startup, ports.Startup),
    ];

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        foreach (var probe in _probes)
        {
            var entries = report.Entries.Where(entry => entry.Value.Tags.Contains(probe.Tag)).ToArray();
            var healthy = entries.All(entry => entry.Value.Status != HealthStatus.Unhealthy);
            if (!probe.SetHealthy(healthy))
            {
                continue;
            }

            if (healthy)
            {
                LogProbeHealthy(logger, probe.Tag, probe.Port);
            }
            else
            {
                var failing = string.Join(", ", entries.Where(e => e.Value.Status == HealthStatus.Unhealthy).Select(e => e.Key));
                LogProbeUnhealthy(logger, probe.Tag, probe.Port, failing);
            }
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var probe in _probes)
        {
            await probe.DisposeAsync();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Probe {Probe} is healthy: port {Port} is open")]
    private static partial void LogProbeHealthy(ILogger logger, string probe, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Probe {Probe} is unhealthy: port {Port} is closed; failing checks: {Failing}")]
    private static partial void LogProbeUnhealthy(ILogger logger, string probe, int port, string failing);

    /// <summary>One port: listening while healthy, closed otherwise. Accepted connections are closed right away.</summary>
    private sealed class ProbeListener(string tag, int port) : IAsyncDisposable
    {
        private readonly Lock _gate = new();
        private TcpListener? _listener;
        private CancellationTokenSource? _stop;
        private Task? _accepting;

        public string Tag { get; } = tag;

        public int Port { get; } = port;

        /// <summary>Opens or closes the port; returns whether the state changed.</summary>
        public bool SetHealthy(bool healthy)
        {
            lock (_gate)
            {
                if (healthy == _listener is not null)
                {
                    return false;
                }

                if (healthy)
                {
                    _listener = new TcpListener(IPAddress.Any, Port);
                    _listener.Start();
                    _stop = new CancellationTokenSource();
                    _accepting = AcceptAsync(_listener, _stop.Token);
                }
                else
                {
                    Close();
                }

                return true;
            }
        }

        private static async Task AcceptAsync(TcpListener listener, CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var client = await listener.AcceptTcpClientAsync(stop);
                }
                catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    return;
                }
            }
        }

        private void Close()
        {
            _stop?.Cancel();
            _listener?.Stop();
            _stop?.Dispose();
            _listener = null;
            _stop = null;
        }

        public async ValueTask DisposeAsync()
        {
            Task? accepting;
            lock (_gate)
            {
                accepting = _accepting;
                Close();
            }

            if (accepting is not null)
            {
                await accepting;
            }
        }
    }
}
