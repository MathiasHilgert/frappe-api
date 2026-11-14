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

    /// <summary>The Valkey connection string.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>The instance name when none is configured.</summary>
    public const string DefaultInstanceName = "frappe";

    /// <summary>The cache key and backplane prefix; unset or empty means <see cref="DefaultInstanceName"/>.</summary>
    public string InstanceName { get; set; } = DefaultInstanceName;
}
