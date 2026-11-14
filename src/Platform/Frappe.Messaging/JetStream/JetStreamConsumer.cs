using System.Reflection;
using Frappe.IntegrationEvents;
using Frappe.Messaging.Inbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Frappe.Messaging.JetStream;

/// <summary>
/// Hosts one durable JetStream consumer for one integration event type. Each message goes through the
/// <see cref="InboxConsumer{TContext, TEvent}"/>; it is acknowledged only after the handler's transaction committed,
/// redelivered with backoff when the handler fails, and moved to <c>frappe.dlq.&lt;module&gt;.&lt;consumer&gt;</c>
/// when it keeps failing or cannot be read at all. Stream, filter subject and dead-letter subject follow
/// <paramref name="naming"/> (<see cref="JetStreamNaming.Default"/> when null).
/// </summary>
/// <param name="js">The JetStream context.</param>
/// <param name="inbox">Runs the handler once per message id.</param>
/// <param name="producerModule">The module whose stream is read, for example <c>geo</c>.</param>
/// <param name="options">Delivery, backoff and dead-letter settings.</param>
/// <param name="logger">The logger.</param>
/// <param name="time">The clock for backoff and heartbeats; the system clock when null.</param>
/// <param name="naming">The stream and subject names; null means the production names.</param>
public sealed partial class JetStreamConsumer<TContext, TEvent>(
    INatsJSContext js,
    InboxConsumer<TContext, TEvent> inbox,
    string producerModule,
    JetStreamConsumerOptions options,
    ILogger<JetStreamConsumer<TContext, TEvent>> logger,
    TimeProvider? time = null,
    JetStreamNaming? naming = null) : BackgroundService
    where TContext : DbContext
    where TEvent : IIntegrationEvent
{
    public const string ErrorHeader = "frappe-error";

    /// <summary>Longest dead-letter reason kept in the header; NATS headers are single lines.</summary>
    public const int MaxReasonLength = 512;

    /// <summary>
    /// Handler failures per message seen by this process. In memory on purpose: after a restart, or when another
    /// instance takes the redelivery, a message gets a fresh budget, which only means more retries, never a lost
    /// message. Bounded so entries left behind by redeliveries to other instances cannot grow without limit.
    /// </summary>
    private const int MaxTrackedFailures = 10_000;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> _handlerFailures = new();

    private readonly JetStreamConsumerOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private readonly JetStreamNaming _naming = naming ?? JetStreamNaming.Default;

    private static readonly string EventType = typeof(TEvent).GetCustomAttribute<IntegrationEventAttribute>()?.Type
        ?? throw new InvalidOperationException($"{typeof(TEvent).Name} has no [IntegrationEvent] attribute.");

    /// <summary>Creates the streams and the durable consumer; idempotent.</summary>
    public async Task<INatsJSConsumer> EnsureConsumerAsync(CancellationToken cancellationToken)
    {
        await JetStreamTopology.EnsureModuleStreamAsync(js, _naming, producerModule, cancellationToken);
        await JetStreamTopology.EnsureDeadLetterStreamAsync(js, _naming, cancellationToken);
        return await js.CreateOrUpdateConsumerAsync(
            _naming.Stream(producerModule),
            new ConsumerConfig(inbox.ConsumerName)
            {
                FilterSubject = _naming.Subject(EventType),
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                AckWait = _options.AckWait,
                DeliverPolicy = _options.DeliverFrom,
            },
            cancellationToken);
    }

    /// <summary>
    /// Never lets an infrastructure failure end the loop: a database restart or a lost broker connection must not
    /// stop the host. A message that could not be settled is redelivered (NAK, or AckWait) and the inbox absorbs
    /// any repetition; if the subscription itself breaks, it is created again after a pause.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var consumer = await EnsureConsumerAsync(stoppingToken);
                var pull = new NatsJSConsumeOpts { MaxMsgs = _options.PrefetchCount };
                await foreach (var message in consumer.ConsumeAsync<byte[]>(opts: pull, cancellationToken: stoppingToken))
                {
                    // Only a working subscription resets the escalation; creating the consumer is not enough.
                    failures = 0;
                    await HandleSafelyAsync(message, stoppingToken);
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                failures++;
                LogSubscriptionFailed(logger, exception, inbox.ConsumerName, failures);
            }

            // Also after a clean end (consumer deleted, subscription closed): never spin, escalate while it keeps failing.
            await Task.Delay(_options.Backoff[Math.Clamp(failures - 1, 0, _options.Backoff.Count - 1)], _time, stoppingToken);
        }
    }

    private async Task HandleSafelyAsync(INatsJSMsg<byte[]> message, CancellationToken cancellationToken)
    {
        try
        {
            await HandleAsync(message, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogDeliveryFailed(logger, exception, inbox.ConsumerName);
            try
            {
                // Escalates like handler failures, so a long outage does not turn into a tight redelivery loop.
                await message.NakAsync(new AckOpts { NakDelay = BackoffFor(message) }, cancellationToken);
            }
            catch (Exception nakFailure) when (!cancellationToken.IsCancellationRequested)
            {
                // The broker is unreachable too; JetStream redelivers after AckWait.
                LogDeliveryFailed(logger, nakFailure, inbox.ConsumerName);
            }
        }
    }

    /// <summary>Processes one delivery and settles it with the broker.</summary>
    public async Task HandleAsync(INatsJSMsg<byte[]> message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var data = message.Data ?? [];

        IncomingMessage incoming;
        try
        {
            incoming = CloudEventHeaders.Read(message.Headers, data);
        }
        catch (FormatException malformed)
        {
            await DeadLetterAsync(message, data, malformed.Message, cancellationToken);
            return;
        }

        var result = await ConsumeKeepingAliveAsync(message, incoming, cancellationToken);
        if (result.Outcome is ConsumeOutcome.Processed or ConsumeOutcome.Duplicate)
        {
            await message.AckAsync(cancellationToken: cancellationToken);
            _handlerFailures.TryRemove(incoming.Id, out _);
            return;
        }

        if (result.Outcome is ConsumeOutcome.Rejected)
        {
            await DeadLetterAsync(message, data, result.Error ?? "unreadable payload", cancellationToken);
            return;
        }

        if (_handlerFailures.Count >= MaxTrackedFailures)
        {
            _handlerFailures.Clear();
        }

        var handlerFailures = _handlerFailures.AddOrUpdate(incoming.Id, 1, (_, count) => count + 1);
        if (handlerFailures >= _options.MaxDeliveries)
        {
            await DeadLetterAsync(message, data, result.Error ?? "handler failed", cancellationToken);
            _handlerFailures.TryRemove(incoming.Id, out _);
            return;
        }

        await message.NakAsync(new AckOpts { NakDelay = BackoffFor(message) }, cancellationToken);
    }

    private TimeSpan BackoffFor(INatsJSMsg<byte[]> message)
    {
        var delivered = (int)(message.Metadata?.NumDelivered ?? 1);
        return _options.Backoff[Math.Clamp(delivered - 1, 0, _options.Backoff.Count - 1)];
    }

    /// <summary>
    /// Tells JetStream the message is still being worked on (every third of AckWait), so a slow handler is not
    /// redelivered to a second worker halfway through.
    /// </summary>
    private async Task<ConsumeResult> ConsumeKeepingAliveAsync(
        INatsJSMsg<byte[]> message, IncomingMessage incoming, CancellationToken cancellationToken)
    {
        using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var handlerTimeout = new CancellationTokenSource(_options.MaxHandlerDuration, _time);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, handlerTimeout.Token);
        var keepAlive = KeepAliveAsync(message, heartbeat.Token);
        try
        {
            // Past MaxHandlerDuration the handler is cancelled: its transaction rolls back, and the attempt counts as a
            // handler failure, so a handler that always hangs ends in the dead-letter stream instead of looping forever.
            return await inbox.ConsumeAsync(incoming, deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new ConsumeResult(ConsumeOutcome.Failed, $"The handler exceeded MaxHandlerDuration ({_options.MaxHandlerDuration}).");
        }
        finally
        {
            await heartbeat.CancelAsync();
            await keepAlive;
        }
    }

    private async Task KeepAliveAsync(INatsJSMsg<byte[]> message, CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_options.AckWait / 3, _time);
            var started = _time.GetTimestamp();
            while (await timer.WaitForNextTickAsync(cancellationToken) && _time.GetElapsedTime(started) < _options.MaxHandlerDuration)
            {
                await message.AckProgressAsync(cancellationToken: cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The handler finished.
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Losing the heartbeat only risks a redelivery, which the inbox absorbs.
            LogDeliveryFailed(logger, exception, inbox.ConsumerName);
        }
    }

    /// <summary>Republishes the original message, with its headers and the reason, then stops redelivery.</summary>
    private async Task DeadLetterAsync(INatsJSMsg<byte[]> message, byte[] data, string reason, CancellationToken cancellationToken)
    {
        var headers = new NatsHeaders();
        if (message.Headers is not null)
        {
            foreach (var (name, values) in message.Headers)
            {
                if (!name.StartsWith("Nats-", StringComparison.OrdinalIgnoreCase))
                {
                    headers[name] = values;
                }
            }
        }

        headers[ErrorHeader] = SingleLine(reason);
        var subject = _naming.DeadLetterSubject(producerModule, inbox.ConsumerName);
        // Deduplicated by consumer and stream sequence: if the terminate below fails and the message comes back,
        // dead-lettering it again does not create a second copy.
        var deadLetterId = $"{inbox.ConsumerName}:{message.Metadata?.Sequence.Stream}";
        var ack = await js.PublishAsync(
            subject,
            data,
            opts: new NatsJSPubOpts { MsgId = deadLetterId },
            headers: headers,
            cancellationToken: cancellationToken);
        try
        {
            ack.EnsureSuccess();
        }
        catch (NatsJSDuplicateMessageException)
        {
            // Already parked by an earlier attempt.
        }

        await message.AckTerminateAsync(cancellationToken: cancellationToken);
        LogDeadLettered(logger, inbox.ConsumerName, subject, reason);
    }

    /// <summary>NATS headers cannot carry line breaks; keep the reason readable and bounded.</summary>
    private static string SingleLine(string reason)
    {
        var flat = string.Join(' ', reason.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return flat.Length <= MaxReasonLength ? flat : flat[..MaxReasonLength];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Consumer {Consumer} could not settle a delivery; it will be redelivered")]
    private static partial void LogDeliveryFailed(ILogger logger, Exception exception, string consumer);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Consumer {Consumer} lost its subscription (failure {Failures}); it will be recreated")]
    private static partial void LogSubscriptionFailed(ILogger logger, Exception exception, string consumer, int failures);

    [LoggerMessage(Level = LogLevel.Error, Message = "Consumer {Consumer} moved a message to {Subject}: {Reason}")]
    private static partial void LogDeadLettered(ILogger logger, string consumer, string subject, string reason);
}
