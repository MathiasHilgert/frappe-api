using System.Data.Common;
using System.Globalization;

namespace Frappe.Geo.Infrastructure.Seed;

/// <summary>
/// Loads the geo reference data from the embedded seed files: per catalogue, upsert the rows and their names (an
/// unchanged row counts as nothing), then retire the rows the file no longer holds; nothing is ever deleted. Currencies
/// go first, because countries reference them.
/// </summary>
internal static class GeoSeed
{
    private const string NamePrefix = "name_";
    private const string Codes = "codes";

    private static readonly string[] _currencyHeader =
    [
        "currency_code",
        "numeric_code",
        "minor_unit",
        "status",
        "name_en",
        "name_es",
        "name_pt",
    ];

    private static readonly string[] _countryHeader =
    [
        "country_code",
        "alpha3",
        "numeric_code",
        "default_currency_code",
        "calling_code",
        "name_en",
        "name_es",
        "name_pt",
    ];

    public static async Task<int> SeedAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        int changed = await CurrenciesAsync(connection, cancellationToken);
        changed += await CountriesAsync(connection, cancellationToken);
        return changed;
    }

    private static async Task<int> CurrenciesAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<string[]> rows = Tsv.Read("currencies.tsv", _currencyHeader);
        string[] codes = Column(rows, 0);
        int changed = await ExecuteAsync(
            connection,
            """
            INSERT INTO geo.currencies (currency_code, numeric_code, minor_unit, status)
            SELECT * FROM unnest(@codes::text[], @numerics::text[], @minors::smallint[], @statuses::text[])
            ON CONFLICT (currency_code) DO UPDATE
            SET numeric_code = excluded.numeric_code, minor_unit = excluded.minor_unit, status = excluded.status
            WHERE (currencies.numeric_code, currencies.minor_unit, currencies.status)
                IS DISTINCT FROM (excluded.numeric_code, excluded.minor_unit, excluded.status)
            """,
            cancellationToken,
            (Codes, codes),
            ("numerics", OptionalColumn(rows, 1)),
            (
                "minors",
                rows.Select(static row =>
                        row[2].Length == 0
                            ? (short?)null
                            : short.Parse(row[2], CultureInfo.InvariantCulture)
                    )
                    .ToArray()
            ),
            ("statuses", Column(rows, 3))
        );
        changed += await NamesAsync(
            connection,
            """
            INSERT INTO geo.currency_names (currency_code, locale, name)
            SELECT * FROM unnest(@keys::text[], @locales::text[], @names::text[])
            ON CONFLICT (currency_code, locale) DO UPDATE SET name = excluded.name
            WHERE currency_names.name IS DISTINCT FROM excluded.name
            """,
            rows,
            _currencyHeader,
            cancellationToken
        );
        changed += await ExecuteAsync(
            connection,
            """
            UPDATE geo.currencies SET status = 'Retired'
            WHERE currency_code <> ALL(@codes::text[]) AND status <> 'Retired'
            """,
            cancellationToken,
            (Codes, codes)
        );
        return changed;
    }

    private static async Task<int> CountriesAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<string[]> rows = Tsv.Read("countries.tsv", _countryHeader);
        string[] codes = Column(rows, 0);
        int changed = await ExecuteAsync(
            connection,
            """
            INSERT INTO geo.countries (country_code, alpha3, numeric_code, default_currency_code, calling_code, status)
            SELECT code, alpha3, numeric_code, currency, calling, 'Active'
            FROM unnest(@codes::text[], @alpha3::text[], @numerics::text[], @currencies::text[], @callings::text[])
                AS seed(code, alpha3, numeric_code, currency, calling)
            ON CONFLICT (country_code) DO UPDATE
            SET alpha3 = excluded.alpha3, numeric_code = excluded.numeric_code,
                default_currency_code = excluded.default_currency_code, calling_code = excluded.calling_code,
                status = excluded.status
            WHERE (countries.alpha3, countries.numeric_code, countries.default_currency_code, countries.calling_code,
                    countries.status)
                IS DISTINCT FROM (excluded.alpha3, excluded.numeric_code, excluded.default_currency_code,
                    excluded.calling_code, excluded.status)
            """,
            cancellationToken,
            (Codes, codes),
            ("alpha3", Column(rows, 1)),
            ("numerics", Column(rows, 2)),
            ("currencies", OptionalColumn(rows, 3)),
            ("callings", OptionalColumn(rows, 4))
        );
        changed += await NamesAsync(
            connection,
            """
            INSERT INTO geo.country_names (country_code, locale, name)
            SELECT * FROM unnest(@keys::text[], @locales::text[], @names::text[])
            ON CONFLICT (country_code, locale) DO UPDATE SET name = excluded.name
            WHERE country_names.name IS DISTINCT FROM excluded.name
            """,
            rows,
            _countryHeader,
            cancellationToken
        );
        changed += await ExecuteAsync(
            connection,
            """
            UPDATE geo.countries SET status = 'Retired'
            WHERE country_code <> ALL(@codes::text[]) AND status <> 'Retired'
            """,
            cancellationToken,
            (Codes, codes)
        );
        return changed;
    }

    // Every name_<locale> column becomes one row per non-empty name, keyed by the row's first column.
    private static Task<int> NamesAsync(
        DbConnection connection,
        string upsert,
        IReadOnlyList<string[]> rows,
        string[] header,
        CancellationToken cancellationToken
    )
    {
        (string Key, string Locale, string Name)[] names =
        [
            .. rows.SelectMany(row =>
                header
                    .Select((column, index) => (column, index))
                    .Where(column =>
                        column.column.StartsWith(NamePrefix, StringComparison.Ordinal)
                        && row[column.index].Length > 0
                    )
                    .Select(column =>
                        (row[0], column.column[NamePrefix.Length..], row[column.index])
                    )
            ),
        ];
        return ExecuteAsync(
            connection,
            upsert,
            cancellationToken,
            ("keys", names.Select(static name => name.Key).ToArray()),
            ("locales", names.Select(static name => name.Locale).ToArray()),
            ("names", names.Select(static name => name.Name).ToArray())
        );
    }

    private static string[] Column(IReadOnlyList<string[]> rows, int index) =>
        [.. rows.Select(row => row[index])];

    private static string?[] OptionalColumn(IReadOnlyList<string[]> rows, int index) =>
        [.. rows.Select(row => row[index].Length == 0 ? null : row[index])];

    private static async Task<int> ExecuteAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters
    )
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
