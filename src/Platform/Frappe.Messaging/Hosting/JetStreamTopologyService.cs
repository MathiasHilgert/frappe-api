using Frappe.Messaging.JetStream;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.JetStream;

namespace Frappe.Messaging.Hosting;

/// <summary>
/// Creates the JetStream streams this host produces into, plus the dead-letter stream. Runs in the background and
/// retries until NATS answers, so a broker that starts later never prevents the host from starting; until then the
/// relay treats publishing as an outage and keeps every message in the outbox.
/// </summary>
internal sealed partial class JetStreamTopologyService(
    INatsJSContext js,
    IEnumerable<ProducerModule> modules,
    TimeProvider time,
    ILogger<JetStreamTopologyService> logger,
    JetStreamNaming naming) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var module in modules)
                {
                    await JetStreamTopology.EnsureModuleStreamAsync(js, naming, module.Name, stoppingToken);
                }

                await JetStreamTopology.EnsureDeadLetterStreamAsync(js, naming, stoppingToken);
                return;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogTopologyFailed(logger, exception, delay);
                await Task.Delay(delay, time, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "JetStream streams could not be ensured; retrying in {Delay}")]
    private static partial void LogTopologyFailed(ILogger logger, Exception exception, TimeSpan delay);
}
