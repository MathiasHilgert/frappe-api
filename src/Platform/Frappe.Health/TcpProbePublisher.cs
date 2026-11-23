using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Frappe.Health;

/// <summary>
/// Answers the liveness and readiness probes over plain TCP: a port accepts connections while its health report is healthy,
/// and refuses them otherwise.
/// </summary>
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
