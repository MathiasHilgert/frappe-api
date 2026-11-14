using Frappe.Geo.Application.Catalog;
using Frappe.Geo.Infrastructure.ReferenceData;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Frappe.Geo.Infrastructure.Persistence;

/// <summary>
/// Geo's seeding, EF Core's recommended way (<c>UseSeeding</c>/<c>UseAsyncSeeding</c>): it runs after every
/// <c>Migrate</c>, under the migration lock, and brings the catalog to the seed through the reference data plan, so it is
/// idempotent and a new seed is applied as a diff. EF Core's tools call the synchronous entry point, so both exist.
/// </summary>
internal static class GeoSeeding
{
    public static TBuilder UseGeoSeeding<TBuilder>(this TBuilder options, ICatalogSeed seed, IClock clock)
        where TBuilder : DbContextOptionsBuilder
    {
        options
            .UseSeeding((db, _) =>
            {
                var catalog = seed.Load();
                var now = clock.GetCurrentInstant();
                Guard(ReferenceDataApplier.Apply(db, CatalogRows.Countries(catalog), now), ReferenceDataApplier.Apply(db, CatalogRows.Currencies(catalog), now));
                db.SaveChanges();
            })
            .UseAsyncSeeding(async (db, _, cancellationToken) =>
            {
                var catalog = seed.Load();
                var now = clock.GetCurrentInstant();
                Guard(
                    await ReferenceDataApplier.ApplyAsync(db, CatalogRows.Countries(catalog), now, cancellationToken),
                    await ReferenceDataApplier.ApplyAsync(db, CatalogRows.Currencies(catalog), now, cancellationToken));
                await db.SaveChangesAsync(cancellationToken);
            });
        return options;
    }

    /// <summary>A seed that would retire too much is a broken seed: the migration fails before saving anything.</summary>
    private static void Guard(params Domain.ReferenceData.ReferenceDataPlan[] plans)
    {
        if (plans.Any(plan => plan.RetiresTooMuch))
        {
            throw new InvalidOperationException("The catalog seed would retire too many countries or currencies; refusing it as broken.");
        }
    }
}
