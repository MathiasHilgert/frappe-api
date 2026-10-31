using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Frappe.Geo.Infrastructure.Timezones;

// An empty catalogue means the migrator's seed never ran, and every branch's zone would fail to resolve.
internal sealed class TimezoneCatalogHealthCheck(GeoDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    ) =>
        await db.Timezones.AnyAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(
                context.Registration.FailureStatus,
                "The timezone catalogue is empty; run the migrator to seed it."
            );
}
