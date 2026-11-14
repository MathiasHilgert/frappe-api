namespace Frappe.Messaging.JetStream;

/// <summary>
/// Where the process's NATS connection goes: <see cref="Url"/> from <c>ConnectionStrings:Nats</c> and
/// <see cref="Namespace"/> from <c>Messaging:Namespace</c>.
/// </summary>
public sealed class JetStreamOptions
{
    /// <summary>The configuration section the hosts bind; the URL is the connection string <see cref="ConnectionStringName"/>.</summary>
    public const string Section = "Messaging";

    /// <summary>The name of the NATS connection string, <c>ConnectionStrings:Nats</c>.</summary>
    public const string ConnectionStringName = "Nats";

    /// <summary>The NATS server URL.</summary>
    public string? Url { get; set; }

    /// <summary>An optional prefix for every stream and subject; null or empty keeps the production names.</summary>
    public string? Namespace { get; set; }
}
