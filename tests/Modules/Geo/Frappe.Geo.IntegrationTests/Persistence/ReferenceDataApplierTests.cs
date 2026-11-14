using Frappe.Geo.Domain.ReferenceData;
using Frappe.Geo.Global.Cldr;
using Frappe.Geo.Infrastructure.Countries;
using Frappe.Geo.Infrastructure.Persistence;
using Frappe.Geo.Infrastructure.ReferenceData;
using Frappe.Localization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Frappe.Geo.IntegrationTests.Persistence;

/// <summary>The plan carried out against Postgres: idempotent, never deletes, retires what a snapshot drops and revives what comes back.</summary>
public sealed class ReferenceDataApplierTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 13, 12, 0);

    private static CountryRow[] Snapshot(Func<CountryRow, bool> keep, Action<CountryRow>? change = null)
    {
        var rows = CatalogRows.Countries(new CldrCatalogSeed().Load()).Where(keep).ToArray();
        foreach (var row in rows)
        {
            change?.Invoke(row);
        }

        return rows;
    }

    private static async Task<ReferenceDataPlan> ApplyAsync(GeoDatabase database, CountryRow[] snapshot)
    {
        await using var db = database.NewContext();
        var plan = await ReferenceDataApplier.ApplyAsync(db, snapshot, Now, CancellationToken.None);
        await db.SaveChangesAsync();
        return plan;
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task The_seed_the_Migrator_applied_is_applied_again_without_a_change(GeoDatabase database)
    {
        var plan = await ApplyAsync(database, Snapshot(_ => true));

        await Assert.That(plan.ChangesAnything).IsFalse();
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_dropped_country_is_retired_not_deleted_and_revived_when_it_comes_back(GeoDatabase database)
    {
        var dropped = await ApplyAsync(database, Snapshot(c => c.Code != "UY"));
        await using (var db = database.NewContext())
        {
            var uruguay = await db.Set<CountryRow>().AsNoTracking().SingleAsync(c => c.Code == "UY");
            await Assert.That(uruguay.RetiredAt).IsEqualTo(Now);
            await Assert.That(uruguay.Names).IsNotEmpty();
        }

        var back = await ApplyAsync(database, Snapshot(_ => true));

        await Assert.That(dropped.Retire).IsEquivalentTo(["UY"]);
        await Assert.That(back.Revive).IsEquivalentTo(["UY"]);
        await Assert.That(back.Insert.Count + back.Update.Count + back.Retire.Count).IsEqualTo(0);
    }

    [Test]
    [ClassDataSource<GeoDatabase>(Shared = SharedType.None)]
    public async Task A_renamed_country_and_a_new_translation_update_its_rows_in_place(GeoDatabase database)
    {
        var plan = await ApplyAsync(database, Snapshot(_ => true, row =>
        {
            if (row.Code == "AR")
            {
                row.Names.Single(n => n.Language.Value == "en").Text = "Argentine Republic";
                row.Names.Add(new CountryNameRow { Language = LanguageTag.Create("es-AR").Value, Text = "República Argentina" });
            }
        }));

        await using var db = database.NewContext();
        var argentina = await db.Set<CountryRow>().AsNoTracking().SingleAsync(c => c.Code == "AR");
        await Assert.That(plan.Update).IsEquivalentTo(["AR"]);
        await Assert.That(argentina.Names.Single(n => n.Language.Value == "en").Text).IsEqualTo("Argentine Republic");
        await Assert.That(argentina.Names.Single(n => n.Language.Value == "es-AR").Text).IsEqualTo("República Argentina");
    }
}
