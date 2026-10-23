using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Frappe.Platform.Health;

/// <summary>
/// Reports status, criticality and latency per dependency. Exception details are deliberately left out:
/// probes are unauthenticated, and failures are already logged by the health check service.
/// </summary>
internal static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.Headers.CacheControl = "no-store";

        var body = new
        {
            status = report.Status.ToString(),
            durationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                criticality = entry.Value.Tags.Contains(HealthTags.Critical, StringComparer.Ordinal) ? HealthTags.Critical : HealthTags.NonCritical,
                durationMs = entry.Value.Duration.TotalMilliseconds,
            }),
        };

        return context.Response.WriteAsJsonAsync(body, JsonSerializerOptions.Web, context.RequestAborted);
    }
}
