using System.ComponentModel.DataAnnotations;

namespace Frappe.Platform.Caching;

/// <summary>
/// Where the shared L2 cache lives. Filled from the <c>valkey</c> connection string rather than a section, and
/// validated when the host starts, not when services are registered.
/// </summary>
public sealed class ValkeyOptions
{
    [Required(ErrorMessage = "Connection string 'valkey' is missing.")]
    public string? ConnectionString { get; set; }
}
