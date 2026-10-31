using System.ComponentModel.DataAnnotations;

namespace Frappe.Platform.Http.RateLimiting;

/// <summary>The rate limits of each policy, bound from the <c>RateLimiting</c> section and validated on start.</summary>
public sealed class RateLimitOptions
{
    /// <summary>The configuration section the options bind from.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>Gets how many requests one client IP may make per <see cref="PublicWindow"/> on a <c>public</c> endpoint.</summary>
    [Range(1, 100_000)]
    public int PublicPermitLimit { get; init; } = 300;

    /// <summary>Gets the fixed window of the <c>public</c> policy.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan PublicWindow { get; init; } = TimeSpan.FromMinutes(1);
}
