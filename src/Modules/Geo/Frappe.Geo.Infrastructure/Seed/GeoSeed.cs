using System.Data.Common;
using System.Globalization;
using Frappe.Geo.Application.Cities;
using Frappe.Geo.Infrastructure.Catalogues;

namespace Frappe.Geo.Infrastructure.Seed;

/// <summary>
/// Loads the geo reference data from the embedded seed files: per catalogue, upsert the rows and their names (an
/// unchanged row counts as nothing), then retire the rows the file no longer holds; nothing is ever deleted. Currencies
/// go first, because countries reference them; subdivisions, cities and locales follow.
/// </summary>
internal static class GeoSeed
{
    private const string NamePrefix = "name_";
    private const string Codes = "codes";

    // Cities go in chunks, so one statement never carries every locality's arrays.
    private const int CityChunk = 1_000;

    private const string CityUpsert = """
        INSERT INTO geo.cities (city_id, indec_id, country_code, subdivision_code, name, department_name,
            search_name, search_text, time_zone_id, status)
        SELECT id, indec, country, subdivision, name, department, search_name, search_text, zone, 'Active'
        FROM unnest(@ids::uuid[], @indecs::text[], @countries::text[], @subdivisions::text[], @names::text[],
            @departments::text[], @searchNames::text[], @searchTexts::text[], @zones::text[])
            AS seed(id, indec, country, subdivision, name, department, search_name, search_text, zone)
        ON CONFLICT (city_id) DO UPDATE
        SET indec_id = excluded.indec_id, country_code = excluded.country_code,
            subdivision_code = excluded.subdivision_code, name = excluded.name,
            department_name = excluded.department_name, search_name = excluded.search_name,
            search_text = excluded.search_text, time_zone_id = excluded.time_zone_id, status = excluded.status
        WHERE (cities.indec_id, cities.country_code, cities.subdivision_code, cities.name,
                cities.department_name, cities.search_name, cities.search_text, cities.time_zone_id,
                cities.status)
            IS DISTINCT FROM (excluded.indec_id, excluded.country_code, excluded.subdivision_code,
                excluded.name, excluded.department_name, excluded.search_name, excluded.search_text,
                excluded.time_zone_id, excluded.status)
        """;

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

    private static readonly string[] _subdivisionHeader =
    [
        "subdivision_code",
        "country_code",
        "name_en",
        "name_es",
        "name_pt",
    ];

    private static readonly string[] _cityHeader =
    [
        "indec_id",
        "name",
        "subdivision_code",
        "department_name",
        "time_zone_id",
    ];

    private static readonly string[] _localeHeader =
    [
        "locale_tag",
        "language_code",
        "text_direction",
        "endonym",
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
        changed += await SubdivisionsAsync(connection, cancellationToken);
        changed += await CitiesAsync(connection, cancellationToken);
        changed += await LocalesAsync(connection, cancellationToken);
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

    private static async Task<int> SubdivisionsAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<string[]> rows = Tsv.Read("subdivisions.tsv", _subdivisionHeader);
        string[] codes = Column(rows, 0);
        int changed = await ExecuteAsync(
            connection,
            """
            INSERT INTO geo.subdivisions (subdivision_code, country_code, status)
            SELECT code, country, 'Active' FROM unnest(@codes::text[], @countries::text[]) AS seed(code, country)
            ON CONFLICT (subdivision_code) DO UPDATE
            SET country_code = excluded.country_code, status = excluded.status
            WHERE (subdivisions.country_code, subdivisions.status)
                IS DISTINCT FROM (excluded.country_code, excluded.status)
            """,
            cancellationToken,
            (Codes, codes),
            ("countries", Column(rows, 1))
        );
        changed += await NamesAsync(
            connection,
            """
            INSERT INTO geo.subdivision_names (subdivision_code, locale, name)
            SELECT * FROM unnest(@keys::text[], @locales::text[], @names::text[])
            ON CONFLICT (subdivision_code, locale) DO UPDATE SET name = excluded.name
            WHERE subdivision_names.name IS DISTINCT FROM excluded.name
            """,
            rows,
            _subdivisionHeader,
            cancellationToken
        );
        changed += await ExecuteAsync(
            connection,
            """
            UPDATE geo.subdivisions SET status = 'Retired'
            WHERE subdivision_code <> ALL(@codes::text[]) AND status <> 'Retired'
            """,
            cancellationToken,
            (Codes, codes)
        );
        return changed;
    }

    // The search text holds the folded locality name and every seeded name of its province and country, so a word of
    // any of them, or a whole typed label, finds the locality.
    private static async Task<int> CitiesAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<string[]> rows = Tsv.Read("cities.tsv", _cityHeader);
        Dictionary<string, string[]> subdivisionNames = FoldedNames(
            Tsv.Read("subdivisions.tsv", _subdivisionHeader),
            _subdivisionHeader
        );
        Dictionary<string, string[]> countryNames = FoldedNames(
            Tsv.Read("countries.tsv", _countryHeader),
            _countryHeader
        );
        int changed = 0;
        foreach (string[][] chunk in rows.Chunk(CityChunk))
        {
            string[] countries = [.. chunk.Select(static row => row[2][..2])];
            string[] searchNames = [.. chunk.Select(static row => SearchText.Fold(row[1]))];
            changed += await ExecuteAsync(
                connection,
                CityUpsert,
                cancellationToken,
                ("ids", chunk.Select(static row => CityIds.From(row[0]).Value).ToArray()),
                ("indecs", Column(chunk, 0)),
                ("countries", countries),
                ("subdivisions", Column(chunk, 2)),
                ("names", Column(chunk, 1)),
                ("departments", OptionalColumn(chunk, 3)),
                ("searchNames", searchNames),
                (
                    "searchTexts",
                    SearchTexts(chunk, searchNames, countries, subdivisionNames, countryNames)
                ),
                ("zones", Column(chunk, 4))
            );
        }

        changed += await ExecuteAsync(
            connection,
            """
            UPDATE geo.cities SET status = 'Retired'
            WHERE indec_id <> ALL(@codes::text[]) AND status <> 'Retired'
            """,
            cancellationToken,
            (Codes, Column(rows, 0))
        );
        return changed;
    }

    private static async Task<int> LocalesAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<string[]> rows = Tsv.Read("locales.tsv", _localeHeader);
        string[] codes = Column(rows, 0);
        int changed = await ExecuteAsync(
            connection,
            """
            INSERT INTO geo.locales (locale_tag, language_code, text_direction, endonym, status)
            SELECT tag, language, direction, endonym, 'Active'
            FROM unnest(@codes::text[], @languages::text[], @directions::text[], @endonyms::text[])
                AS seed(tag, language, direction, endonym)
            ON CONFLICT (locale_tag) DO UPDATE
            SET language_code = excluded.language_code, text_direction = excluded.text_direction,
                endonym = excluded.endonym, status = excluded.status
            WHERE (locales.language_code, locales.text_direction, locales.endonym, locales.status)
                IS DISTINCT FROM (excluded.language_code, excluded.text_direction, excluded.endonym, excluded.status)
            """,
            cancellationToken,
            (Codes, codes),
            ("languages", Column(rows, 1)),
            ("directions", Column(rows, 2)),
            ("endonyms", Column(rows, 3))
        );
        changed += await NamesAsync(
            connection,
            """
            INSERT INTO geo.locale_names (locale_tag, locale, name)
            SELECT * FROM unnest(@keys::text[], @locales::text[], @names::text[])
            ON CONFLICT (locale_tag, locale) DO UPDATE SET name = excluded.name
            WHERE locale_names.name IS DISTINCT FROM excluded.name
            """,
            rows,
            _localeHeader,
            cancellationToken
        );
        changed += await ExecuteAsync(
            connection,
            """
            UPDATE geo.locales SET status = 'Retired'
            WHERE locale_tag <> ALL(@codes::text[]) AND status <> 'Retired'
            """,
            cancellationToken,
            (Codes, codes)
        );
        return changed;
    }

    private static string[] SearchTexts(
        string[][] chunk,
        string[] searchNames,
        string[] countries,
        Dictionary<string, string[]> subdivisionNames,
        Dictionary<string, string[]> countryNames
    ) =>
        [
            .. chunk.Select(
                (row, index) =>
                    string.Join(
                        ' ',
                        new[] { searchNames[index] }
                            .Concat(subdivisionNames[row[2]])
                            .Concat(countryNames[countries[index]])
                            .Distinct(StringComparer.Ordinal)
                    )
            ),
        ];

    // The distinct folded names of each row, from its name_<locale> columns, keyed by its first column.
    private static Dictionary<string, string[]> FoldedNames(
        IReadOnlyList<string[]> rows,
        string[] header
    ) =>
        rows.ToDictionary(
            static row => row[0],
            row =>
                header
                    .Select((column, index) => (column, index))
                    .Where(column =>
                        column.column.StartsWith(NamePrefix, StringComparison.Ordinal)
                        && row[column.index].Length > 0
                    )
                    .Select(column => SearchText.Fold(row[column.index]))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
            StringComparer.Ordinal
        );

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

    private static string[] Column(IReadOnlyCollection<string[]> rows, int index) =>
        [.. rows.Select(row => row[index])];

    private static string?[] OptionalColumn(IReadOnlyCollection<string[]> rows, int index) =>
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
