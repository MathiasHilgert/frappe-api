using System.ComponentModel.DataAnnotations;

namespace Frappe.Identity.Infrastructure.Security;

/// <summary>
/// How long one limiter call may wait on Valkey before failing open. Kept separate from the shared Valkey client
/// timeouts (which the cache also uses), so an outage adds at most this much to a sign-in.
/// </summary>
internal sealed class AttemptLimiterOptions
{
    public const string SectionName = "Identity:AttemptLimiter";

    [Range(typeof(TimeSpan), "00:00:00.010", "00:00:05")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMilliseconds(250);
}
