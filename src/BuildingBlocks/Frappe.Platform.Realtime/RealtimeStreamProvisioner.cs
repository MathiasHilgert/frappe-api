using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Frappe.Platform.Realtime;

/// <summary>
/// Creates the JetStream stream in the background, retrying with backoff: NATS is non-critical,
/// so its outage must never stop the instance from starting.
/// </summary>
internal sealed partial class RealtimeStreamProvisioner(INatsJSContext jetStream, ILogger<RealtimeStreamProvisioner> logger)
    : BackgroundService
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    // Replays are served for 24h; deduplicating over the same window means an outbox redelivery inside it is
    // never pushed twice.
    internal static StreamConfig StreamConfig => new(RealtimeSubjects.StreamName, [RealtimeSubjects.All])
    {
        MaxAge = TimeSpan.FromHours(24),
        DuplicateWindow = TimeSpan.FromHours(24),
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await jetStream.CreateOrUpdateStreamAsync(StreamConfig, stoppingToken);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogRetry(logger, backoff, ex);
                await Task.Delay(backoff, stoppingToken);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Realtime stream not provisioned yet; retrying in {Backoff}")]
    private static partial void LogRetry(ILogger logger, TimeSpan backoff, Exception error);
}
