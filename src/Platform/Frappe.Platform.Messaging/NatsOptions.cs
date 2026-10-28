using System.ComponentModel.DataAnnotations;

namespace Frappe.Platform.Messaging;

/// <summary>
/// The NATS JetStream connection and topology every integration event travels through, bound from the
/// <c>Nats</c> section and validated on start.
/// </summary>
public sealed class NatsOptions
{
    /// <summary>The configuration section the options bind from.</summary>
    public const string SectionName = "Nats";

    /// <summary>
    /// Gets the server URL without credentials, such as <c>tls://connect.example.com:4222</c>; a <c>tls://</c>
    /// URL requires TLS.
    /// </summary>
    [Required]
    [NatsUrl]
    public string Url { get; init; } = "";

    /// <summary>
    /// Gets the content of the NATS credentials file (user JWT and NKey seed), or <see langword="null"/> for a
    /// server without authentication, as local development runs. Bound as content so a secret store can supply it.
    /// </summary>
    public string? Creds { get; init; }

    /// <summary>Gets the stream every integration event is stored in.</summary>
    [Required]
    [RegularExpression("^[A-Z][A-Z0-9_]*$")]
    public string Stream { get; init; } = "FRAPPE_EVENTS";

    /// <summary>Gets the first token of every subject; the stream captures <c>&lt;root&gt;.&gt;</c>.</summary>
    [Required]
    [RegularExpression("^[a-z][a-z0-9_]*$")]
    public string SubjectRoot { get; init; } = "frappe";

    /// <summary>Gets the first segment of every durable consumer name.</summary>
    [Required]
    [RegularExpression("^[a-z][a-z0-9_]*$")]
    public string ConsumerPrefix { get; init; } = "frappe";

    /// <summary>Gets the stream's replica count: 3 in deployed environments, 1 locally.</summary>
    [Range(1, 5)]
    public int Replicas { get; init; } = 1;
}
