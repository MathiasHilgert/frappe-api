namespace Frappe.Messaging.JetStream;

/// <summary>
/// The names of every stream and subject this process uses. <see cref="Default"/> is the production topology
/// (<c>FRAPPE_GEO</c> over <c>frappe.geo.&gt;</c>, <c>FRAPPE_DLQ</c> over <c>frappe.dlq.&gt;</c>, subjects equal to the
/// CloudEvents types). A <see cref="Namespace"/> prefixes all of them, so several isolated topologies can share one
/// NATS server: with <c>t1a2b</c> the module stream is <c>T1A2B_FRAPPE_GEO</c> over <c>t1a2b.frappe.geo.&gt;</c>.
/// The CloudEvents <c>type</c> of a message never carries the namespace.
/// </summary>
public sealed record JetStreamNaming
{
    /// <summary>The production names: no namespace.</summary>
    public static JetStreamNaming Default { get; } = new(string.Empty);

    /// <summary>
    /// Creates the naming for <paramref name="namespace"/>; empty means <see cref="Default"/>. A namespace is a single
    /// subject token made of lowercase letters, digits, <c>-</c> and <c>_</c>. Lowercase only: stream names are
    /// uppercased, so namespaces that differ only in case would share a stream.
    /// </summary>
    /// <param name="namespace">The prefix shared by every stream and subject, or empty for the production names.</param>
    public JetStreamNaming(string @namespace)
    {
        ArgumentNullException.ThrowIfNull(@namespace);
        if (!@namespace.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '_'))
        {
            throw new ArgumentException(
                $"The JetStream namespace '{@namespace}' may only contain lowercase ASCII letters, digits, '-' and '_'.", nameof(@namespace));
        }

        Namespace = @namespace;
    }

    /// <summary>The prefix of every stream and subject; empty in production.</summary>
    public string Namespace { get; }

    /// <summary>The shared dead-letter stream: <c>FRAPPE_DLQ</c>, or <c>&lt;NAMESPACE&gt;_FRAPPE_DLQ</c>.</summary>
    public string DeadLetterStream => StreamName(JetStreamTopology.DeadLetterStream);

    /// <summary>The subjects the dead-letter stream captures: <c>frappe.dlq.&gt;</c>, namespaced.</summary>
    public string DeadLetterSubjects => Subject("frappe.dlq.>");

    /// <summary>A producer module's stream: <c>FRAPPE_&lt;MODULE&gt;</c>, namespaced.</summary>
    /// <param name="module">The producer module, for example <c>geo</c>.</param>
    public string Stream(string module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        return StreamName($"FRAPPE_{module.ToUpperInvariant()}");
    }

    /// <summary>The subjects a module stream captures: <c>frappe.&lt;module&gt;.&gt;</c>, namespaced.</summary>
    /// <param name="module">The producer module, for example <c>geo</c>.</param>
    public string ModuleSubjects(string module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        return Subject($"frappe.{module}.>");
    }

    /// <summary>The subject an event is published on: its CloudEvents type, namespaced.</summary>
    /// <param name="eventType">The CloudEvents type, for example <c>frappe.geo.holidays_synced.v1</c>.</param>
    public string Subject(string eventType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        return Namespace.Length == 0 ? eventType : $"{Namespace}.{eventType}";
    }

    /// <summary>Where a consumer parks what it cannot process: <c>frappe.dlq.&lt;module&gt;.&lt;consumer&gt;</c>, namespaced.</summary>
    /// <param name="module">The producer module the consumer reads from.</param>
    /// <param name="consumer">The durable consumer name.</param>
    public string DeadLetterSubject(string module, string consumer) => Subject($"frappe.dlq.{module}.{consumer}");

    private string StreamName(string name) => Namespace.Length == 0 ? name : $"{Namespace.ToUpperInvariant()}_{name}";
}
