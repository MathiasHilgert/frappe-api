using NodaTime.TimeZones;

namespace Frappe.Geo.UnitTests;

public sealed class TzdbTests
{
    // Argentine and other zone rules changed through 2025b; an older tzdb would resolve wrong offsets.
    private const string MinimumVersion = "2025b";

    [Fact]
    public void The_tzdb_in_use_is_at_least_2025b()
    {
        string version = TzdbDateTimeZoneSource.Default.TzdbVersion;

        string.CompareOrdinal(version, MinimumVersion).ShouldBeGreaterThanOrEqualTo(0);
    }
}
