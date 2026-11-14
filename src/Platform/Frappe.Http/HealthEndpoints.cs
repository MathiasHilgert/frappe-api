using System.Text.Json;
using Frappe.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Frappe.Http;

/// <summary>
/// The Kubernetes probes over HTTP (S23): <c>/health/live</c>, <c>/health/ready</c> and <c>/health/startup</c>.
/// They answer 200 when healthy or degraded and 503 when unhealthy, with the status and each check by name: no hosts,
/// errors or connection strings, which go to the logs and traces instead. No rate limit and no language: a probe
/// must always be answered.
/// </summary>
public static class HealthEndpoints
{
    public const string Prefix = "/health";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapFrappeHealth(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var health = endpoints.MapGroup(Prefix).WithTags("Health").ExcludeFromDescription();
        foreach (var probe in new[] { HealthProbes.Live, HealthProbes.Ready, HealthProbes.Startup })
        {
            health.MapHealthChecks($"/{probe}", new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains(probe),
                ResponseWriter = WriteAsync,
            });
        }

        return endpoints;
    }

    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-store";
        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
                }),
            },
            Json,
            context.RequestAborted);
    }
}
