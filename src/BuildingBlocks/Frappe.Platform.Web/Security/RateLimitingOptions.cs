using System.ComponentModel.DataAnnotations;

namespace Frappe.Platform.Web.Security;

/// <summary>
/// The in-process per-IP backstop on anonymous auth endpoints. It needs no Valkey, so it still holds when the
/// attempt limiter fails open; the defaults sit well above what one person (or one restaurant's Wi-Fi) ever sends.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "Platform:RateLimiting";

    [Range(1, 100_000)]
    public int PermitLimit { get; set; } = 300;

    [Range(1, 3_600)]
    public int WindowSeconds { get; set; } = 60;
}
