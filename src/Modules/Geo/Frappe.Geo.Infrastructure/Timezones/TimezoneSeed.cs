using System.Data.Common;
using NodaTime.TimeZones;

namespace Frappe.Geo.Infrastructure.Timezones;

// The catalogue is the TZDB release NodaTime ships, so a NodaTime upgrade is the refresh; no data file exists.
internal static class TimezoneSeed
{
    // One command: the self-referencing FK is checked at the INSERT's end, so an alias may arrive before its
    // canonical zone. Rows the release no longer lists are kept, so a stored reference to a retired id still
    // resolves; the UPDATE walks each alias's chain to the zone at its root and re-points a kept alias whose zone
    // this or an earlier release made an alias, whatever the chain's depth, so no alias ever points at an alias.
    // UNION drops repeated rows, so even a cyclic chain ends.
    private const string Upsert = """
        INSERT INTO geo.timezones (name, canonical_name)
        SELECT name, canonical_name FROM unnest(@names, @canonical_names) AS source (name, canonical_name)
        ON CONFLICT (name) DO UPDATE SET canonical_name = excluded.canonical_name
        WHERE timezones.canonical_name IS DISTINCT FROM excluded.canonical_name;
        WITH RECURSIVE chain (name, target) AS (
            SELECT name, canonical_name FROM geo.timezones WHERE canonical_name IS NOT NULL
            UNION
            SELECT chain.name, hop.canonical_name FROM chain
            JOIN geo.timezones AS hop ON hop.name = chain.target
            WHERE hop.canonical_name IS NOT NULL
        )
        UPDATE geo.timezones AS alias SET canonical_name = chain.target
        FROM chain JOIN geo.timezones AS root ON root.name = chain.target AND root.canonical_name IS NULL
        WHERE alias.name = chain.name AND alias.canonical_name <> chain.target
        """;

    public static async Task<int> UpsertAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        // The map sends every id to its canonical id, so a canonical zone maps to itself and gets no canonical name.
        KeyValuePair<string, string>[] zones = [.. TzdbDateTimeZoneSource.Default.CanonicalIdMap];
        await using DbCommand upsert = connection.CreateCommand();
        upsert.CommandText = Upsert;
        upsert.Parameters.Add(
            Parameter(upsert, "names", [.. zones.Select(static zone => zone.Key)])
        );
        upsert.Parameters.Add(
            Parameter(
                upsert,
                "canonical_names",
                [
                    .. zones.Select(static zone =>
                        string.Equals(zone.Key, zone.Value, StringComparison.Ordinal)
                            ? null
                            : zone.Value
                    ),
                ]
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
