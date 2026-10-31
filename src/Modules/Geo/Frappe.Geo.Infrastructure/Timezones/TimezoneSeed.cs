using System.Data.Common;
using Frappe.Geo.Domain.Timezones;
using NodaTime.TimeZones;

namespace Frappe.Geo.Infrastructure.Timezones;

// The catalogue is the TZDB release NodaTime ships, so a NodaTime upgrade is the refresh; no data file exists.
internal static class TimezoneSeed
{
    // One statement: the self-referencing FK is checked at its end, so an alias may arrive before its canonical
    // zone. Rows the release no longer lists are kept, so a stored reference to a retired id still resolves.
    private const string Upsert = """
        INSERT INTO geo.timezones (name, canonical_name)
        SELECT name, canonical_name FROM unnest(@names, @canonical_names) AS source (name, canonical_name)
        ON CONFLICT (name) DO UPDATE SET canonical_name = excluded.canonical_name
        WHERE timezones.canonical_name IS DISTINCT FROM excluded.canonical_name
        """;

    public static async Task<int> UpsertAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<Timezone> zones = Timezone.FromTzdb(TzdbDateTimeZoneSource.Default);
        await using DbCommand upsert = connection.CreateCommand();
        upsert.CommandText = Upsert;
        upsert.Parameters.Add(
            Parameter(upsert, "names", [.. zones.Select(static zone => zone.Name)])
        );
        upsert.Parameters.Add(
            Parameter(
                upsert,
                "canonical_names",
                [.. zones.Select(static zone => zone.CanonicalName)]
            )
        );
        return await upsert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DbParameter Parameter(DbCommand command, string name, string?[] values)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = values;
        return parameter;
    }
}
