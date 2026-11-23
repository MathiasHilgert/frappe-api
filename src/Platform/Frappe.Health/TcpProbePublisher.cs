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
        new(HealthProbes.Live, ports.Live, logger),
        new(HealthProbes.Ready, ports.Ready, logger),
        new(HealthProbes.Startup, ports.Startup, logger),
    ];

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        foreach (var probe in _probes)
        {
            var entries = report.Entries.Where(entry => entry.Value.Tags.Contains(probe.Tag)).ToArray();
            var healthy = entries.All(entry => entry.Value.Status != HealthStatus.Unhealthy);
            bool changed;
            try
            {
                changed = probe.SetHealthy(healthy);
            }
            catch (SocketException exception)
            {
                // The port stays closed and nothing is marked open, so the next publish tries to open it again.
                LogProbeOpenFailed(logger, exception, probe.Tag, probe.Port);
                continue;
            }

            if (!changed)
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

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Probe {Probe} could not open port {Port}; it stays closed and is tried again at the next publish")]
    private static partial void LogProbeOpenFailed(ILogger logger, Exception exception, string probe, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Probe {Probe} stopped accepting connections on port {Port}")]
    private static partial void LogAcceptStopped(ILogger logger, Exception exception, string probe, int port);

    /// <summary>One port: listening while healthy, closed otherwise. Accepted connections are closed right away.</summary>
    private sealed class ProbeListener(string tag, int port, ILogger logger) : IAsyncDisposable
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
                    var listener = new TcpListener(IPAddress.Any, Port);
                    try
                    {
                        listener.Start();
                    }
                    catch (SocketException)
                    {
                        listener.Stop();
                        throw;
                    }

                    _listener = listener;
                    _stop = new CancellationTokenSource();
                    _accepting = AcceptAsync(listener, _stop.Token);
                }
                else
                {
                    Close();
                }

                return true;
            }
        }

        private async Task AcceptAsync(TcpListener listener, CancellationToken stop)
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var client = await listener.AcceptTcpClientAsync(stop);
                }
                catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException exception)
                {
                    if (!stop.IsCancellationRequested)
                    {
                        LogAcceptStopped(logger, exception, Tag, Port);
                    }

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
