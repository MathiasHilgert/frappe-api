using Frappe.Messaging.Relay;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Frappe.Messaging.Hosting;

/// <summary>
/// Runs the relay for the life of the host: a full batch means there may be more, so it goes again at once;
/// otherwise it waits <see cref="OutboxRelayOptions.PollInterval"/>. An unexpected failure (for example the database
/// being unreachable) is logged and retried with growing pauses; it never stops the host.
/// </summary>
internal sealed partial class OutboxRelayService<TContext>(
    OutboxRelay<TContext> relay,
    OutboxRelayOptions options,
    TimeProvider time,
    ILogger<OutboxRelayService<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan pause;
            try
            {
                var published = await relay.PublishPendingAsync(stoppingToken);
                failures = 0;
                pause = published >= options.BatchSize ? TimeSpan.Zero : options.PollInterval;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                failures++;
                LogPassFailed(logger, exception, typeof(TContext).Name, failures);
                pause = options.RetryBackoff[Math.Min(failures - 1, options.RetryBackoff.Count - 1)];
            }

            if (pause > TimeSpan.Zero)
            {
                await Task.Delay(pause, time, stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox relay for {Context} failed (failure {Failures}); retrying")]
    private static partial void LogPassFailed(ILogger logger, Exception exception, string context, int failures);
}
