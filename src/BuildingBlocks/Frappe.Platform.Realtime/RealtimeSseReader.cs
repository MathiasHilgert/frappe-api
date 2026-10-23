using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Frappe.Platform.Realtime;

/// <summary>
/// Reads one business's notifications. The stream sequence is the SSE event id, so a client reconnecting
/// with Last-Event-ID resumes exactly after the last event it saw.
/// </summary>
internal sealed partial class RealtimeSseReader(INatsJSContext jetStream, ILogger<RealtimeSseReader> logger)
{
    public async IAsyncEnumerable<SseItem<JsonElement>> ReadAsync(
        Guid businessId, ulong? lastEventId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var resumeFrom = lastEventId is { } seen && seen < ulong.MaxValue ? seen + 1 : (ulong?)null;

        var consumer = await jetStream.CreateOrderedConsumerAsync(
            RealtimeSubjects.StreamName,
            new NatsJSOrderedConsumerOpts
            {
                FilterSubjects = [RealtimeSubjects.ForBusiness(businessId)],
                DeliverPolicy = resumeFrom is null ? ConsumerConfigDeliverPolicy.New : ConsumerConfigDeliverPolicy.ByStartSequence,
                OptStartSeq = resumeFrom ?? 0,
            },
            cancellationToken);

        await foreach (var msg in consumer.ConsumeAsync<JsonElement>(cancellationToken: cancellationToken))
        {
            // Without a sequence the client could not resume; with a bad payload it would get an empty event.
            if (msg.Metadata?.Sequence.Stream is not { } sequence || msg.Error is not null)
            {
                LogSkipped(logger, msg.Subject, msg.Error);
                continue;
            }

            yield return new SseItem<JsonElement>(msg.Data, RealtimeSubjects.TopicOf(businessId, msg.Subject))
            {
                EventId = sequence.ToString(CultureInfo.InvariantCulture),
            };
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped realtime message on {Subject}")]
    private static partial void LogSkipped(ILogger logger, string subject, Exception? error);
}
