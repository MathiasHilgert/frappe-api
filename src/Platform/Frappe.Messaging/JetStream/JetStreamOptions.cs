using System.ComponentModel.DataAnnotations;

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

    /// <summary>
    /// <c>ConnectionStrings:Nats</c>: the NATS server URL, such as <c>nats://nats:4222</c> (read from the
    /// connection strings section, not from <c>Messaging</c>). Required, no default; a missing value fails at
    /// startup. Changing it points the process at another broker; its streams and consumers are created there.
    /// </summary>
    [Required(ErrorMessage = "Configuration 'ConnectionStrings:Nats' is required.")]
    public string? Url { get; set; }

    /// <summary>
    /// <c>Messaging:Namespace</c>: an optional prefix for every stream and subject of the process. Default empty,
    /// which keeps the production names. Range any text valid in a NATS subject token. Set it only to isolate
    /// several topologies on one server (tests do); changing it on a live system abandons the streams under the old
    /// names.
    /// </summary>
    public string? Namespace { get; set; }
}
