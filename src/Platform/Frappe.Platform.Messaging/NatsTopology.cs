using System.Reflection;
using System.Text.RegularExpressions;
using Frappe.Kernel.Messaging;
using JasperFx.Core;

namespace Frappe.Platform.Messaging;

/// <summary>
/// Where each integration event lives on NATS: its subject <c>&lt;root&gt;.&lt;module&gt;.&lt;entity&gt;.&lt;fact&gt;.v&lt;N&gt;</c>,
/// taken from its <see cref="MessageIdentityAttribute"/>, and the durable consumer that reads it, plus the limits of
/// the stream that stores every event.
/// </summary>
public static partial class NatsTopology
{
    /// <summary>The most bytes the stream keeps before it drops the oldest events.</summary>
    public const long MaxBytes = 10L * 1024 * 1024 * 1024;

    /// <summary>The largest event the stream accepts; bigger content travels as a claim check.</summary>
    public const int MaxMessageSize = 1024 * 1024;

    /// <summary>
    /// Unlimited deliveries: a consumer never skips an event, so an inbox outage delays it but never drops it.
    /// </summary>
    public const int MaxDeliver = -1;

    /// <summary>The parallelism of each NATS listener.</summary>
    public const int ListenerParallelism = 4;

    /// <summary>How long the stream keeps an event; with <c>event_log</c> it is the replay source.</summary>
    public static readonly TimeSpan MaxAge = 30.Days();

    /// <summary>
    /// How long the stream drops a second publish with the same <c>Nats-Msg-Id</c> (the envelope id); the
    /// inbox keeps handled ids at least as long (<c>KeepAfterMessageHandling</c>).
    /// </summary>
    public static readonly TimeSpan DuplicateWindow = 15.Minutes();

    /// <summary>How long the server waits for an acknowledgement before it delivers an event again.</summary>
    public static readonly TimeSpan AckWait = 30.Seconds();

    /// <summary>Gets the subject an integration event is published on.</summary>
    /// <param name="root">The first token, <see cref="NatsOptions.SubjectRoot"/>.</param>
    /// <param name="eventType">The integration event type.</param>
    /// <exception cref="InvalidOperationException">The event's identity does not make a five-token subject.</exception>
    public static string Subject(string root, Type eventType) => $"{root}.{Identity(eventType)}";

    /// <summary>Gets the durable consumer that reads an integration event: <c>&lt;prefix&gt;-module-entity-fact-vN</c>.</summary>
    /// <param name="prefix">The first segment, <see cref="NatsOptions.ConsumerPrefix"/>.</param>
    /// <param name="eventType">The integration event type.</param>
    /// <exception cref="InvalidOperationException">The event's identity does not make a five-token subject.</exception>
    public static string Consumer(string prefix, Type eventType) =>
        $"{prefix}-{Identity(eventType).Replace('.', '-')}";

    private static string Identity(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        string? name = eventType.GetCustomAttribute<MessageIdentityAttribute>()?.Name;
        return name is not null && EventIdentity().IsMatch(name)
            ? name
            : throw new InvalidOperationException(
                $"Integration event {eventType.FullName} needs [MessageIdentity(\"module.entity.fact.vN\")]: four "
                    + "lowercase tokens ending in a version, so its NATS subject has five; it has "
                    + $"'{name ?? "none"}'."
            );
    }

    [GeneratedRegex(
        "^[a-z][a-z0-9_]*\\.[a-z][a-z0-9_]*\\.[a-z][a-z0-9_]*\\.v[0-9]+$",
        RegexOptions.None,
        100
    )]
    private static partial Regex EventIdentity();
}
