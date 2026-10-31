using Frappe.Geo.Domain.Timezones;
using NodaTime.TimeZones;

namespace Frappe.Geo.UnitTests.Timezones;

public sealed class TimezoneCatalogTests
{
    [Fact]
    public void A_canonical_zone_has_no_canonical_name()
    {
        IReadOnlyList<Timezone> catalogue = Timezone.FromTzdb(TzdbDateTimeZoneSource.Default);

        catalogue
            .Single(static zone =>
                string.Equals(zone.Name, "America/Argentina/Cordoba", StringComparison.Ordinal)
            )
            .CanonicalName.ShouldBeNull();
    }

    [Fact]
    public void An_alias_names_the_canonical_zone_it_links_to()
    {
        IReadOnlyList<Timezone> catalogue = Timezone.FromTzdb(TzdbDateTimeZoneSource.Default);

        catalogue
            .Single(static zone =>
                string.Equals(zone.Name, "America/Cordoba", StringComparison.Ordinal)
            )
            .CanonicalName.ShouldBe("America/Argentina/Cordoba");
    }

    [Fact]
    public void The_catalogue_lists_every_id_of_the_tzdb_release_once()
    {
        IReadOnlyList<Timezone> catalogue = Timezone.FromTzdb(TzdbDateTimeZoneSource.Default);

        catalogue
            .Select(static zone => zone.Name)
            .ShouldBe(TzdbDateTimeZoneSource.Default.CanonicalIdMap.Keys, ignoreOrder: true);
    }
}
