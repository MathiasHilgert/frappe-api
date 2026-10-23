namespace Frappe.Platform.Caching;

/// <summary>Where the shared L2 cache lives. Validated when the host starts, not when services are registered.</summary>
public sealed class ValkeyOptions
{
    public string? ConnectionString { get; set; }
}
