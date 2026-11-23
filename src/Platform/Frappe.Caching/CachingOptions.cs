using System.ComponentModel.DataAnnotations;

namespace Frappe.Caching;

/// <summary>
/// Where the shared cache lives: <see cref="ConnectionString"/> from <c>ConnectionStrings:Valkey</c> (required) and
/// <see cref="InstanceName"/> from <c>Caching:InstanceName</c> (default <c>frappe</c>), the cache key and backplane
/// prefix, for sharing one Valkey between environments.
/// </summary>
public sealed class CachingOptions
{
    /// <summary>The configuration section the hosts bind; the connection string is <see cref="ConnectionStringName"/>.</summary>
    public const string Section = "Caching";

    /// <summary>The name of the Valkey connection string, <c>ConnectionStrings:Valkey</c>.</summary>
    public const string ConnectionStringName = "Valkey";

    /// <summary>
    /// <c>ConnectionStrings:Valkey</c>: the Valkey connection string (read from the connection strings section, not
    /// from <c>Caching</c>). Required, no default; a missing value fails at startup. Pointing it at another server
    /// moves the shared cache, the backplane and the rate limit counters there, and empties them.
    /// </summary>
    [Required(ErrorMessage = "Configuration 'ConnectionStrings:Valkey' is required.")]
    public string? ConnectionString { get; set; }

    /// <summary>The instance name when none is configured.</summary>
    public const string DefaultInstanceName = "frappe";

    /// <summary>
    /// <c>Caching:InstanceName</c>: the prefix of every cache key and backplane channel, so environments may share
    /// one Valkey. Default <c>frappe</c> (also used when unset or empty). Range any non-empty text without spaces.
    /// Changing it abandons the entries under the old prefix: they are never read again and expire on their own.
    /// </summary>
    public string InstanceName { get; set; } = DefaultInstanceName;
}
