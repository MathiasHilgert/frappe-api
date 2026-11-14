using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Frappe.Messaging.JetStream;

/// <summary>
/// One stream per producing module (<c>FRAPPE_GEO</c> over <c>frappe.geo.&gt;</c>) and one shared dead-letter stream
/// (<c>FRAPPE_DLQ</c> over <c>frappe.dlq.&gt;</c>). Subjects are the CloudEvents types, so a consumer filters by type.
/// The names come from a <see cref="JetStreamNaming"/>; the overloads without one use <see cref="JetStreamNaming.Default"/>.
/// </summary>
public static class JetStreamTopology
{
    /// <summary>The production dead-letter stream; a namespaced one is <see cref="JetStreamNaming.DeadLetterStream"/>.</summary>
    public const string DeadLetterStream = "FRAPPE_DLQ";

    /// <summary>
    /// Limits retention, not work-queue: several consumers (projection, cache, other modules) read the same events.
    /// Seven days covers a long outage of any consumer; the outbox, not the stream, is the source of truth.
    /// </summary>
    public static readonly TimeSpan ModuleRetention = TimeSpan.FromDays(7);

    /// <summary>Broker-side dedupe of relay retries (same <c>Nats-Msg-Id</c>); the inbox covers the rest.</summary>
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan DeadLetterRetention = TimeSpan.FromDays(30);

    /// <summary>The production stream of <paramref name="module"/>; see <see cref="JetStreamNaming.Stream"/>.</summary>
    public static string StreamOf(string module) => JetStreamNaming.Default.Stream(module);

    /// <summary>The production dead-letter subject; see <see cref="JetStreamNaming.DeadLetterSubject"/>.</summary>
    public static string DeadLetterSubject(string module, string consumer) => JetStreamNaming.Default.DeadLetterSubject(module, consumer);

    /// <summary>Creates or updates a module stream with the production names.</summary>
    public static Task EnsureModuleStreamAsync(INatsJSContext js, string module, CancellationToken cancellationToken) =>
        EnsureModuleStreamAsync(js, JetStreamNaming.Default, module, cancellationToken);

    /// <summary>Creates or updates <paramref name="module"/>'s stream under <paramref name="naming"/>.</summary>
    public static async Task EnsureModuleStreamAsync(INatsJSContext js, JetStreamNaming naming, string module, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(js);
        ArgumentNullException.ThrowIfNull(naming);
        await js.CreateOrUpdateStreamAsync(
            new StreamConfig(naming.Stream(module), [naming.ModuleSubjects(module)])
            {
                Retention = StreamConfigRetention.Limits,
                Storage = StreamConfigStorage.File,
                MaxAge = ModuleRetention,
                DuplicateWindow = DuplicateWindow,
            },
            cancellationToken);
    }

    /// <summary>Creates or updates the dead-letter stream with the production names.</summary>
    public static Task EnsureDeadLetterStreamAsync(INatsJSContext js, CancellationToken cancellationToken) =>
        EnsureDeadLetterStreamAsync(js, JetStreamNaming.Default, cancellationToken);

    /// <summary>Creates or updates the dead-letter stream under <paramref name="naming"/>.</summary>
    public static async Task EnsureDeadLetterStreamAsync(INatsJSContext js, JetStreamNaming naming, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(js);
        ArgumentNullException.ThrowIfNull(naming);
        await js.CreateOrUpdateStreamAsync(
            new StreamConfig(naming.DeadLetterStream, [naming.DeadLetterSubjects])
            {
                Retention = StreamConfigRetention.Limits,
                Storage = StreamConfigStorage.File,
                MaxAge = DeadLetterRetention,
            },
            cancellationToken);
    }
}
