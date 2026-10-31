using System.Data.Common;
using System.Globalization;
using Frappe.Geo.Application.Cities;
using Frappe.Geo.Domain.Addresses;
using Frappe.Geo.Infrastructure.Catalogues;

namespace Frappe.Geo.Infrastructure.Seed;

/// <summary>
/// Loads the geo reference data from the embedded seed files: per catalogue, upsert the rows and their names (an
/// unchanged row counts as nothing), then retire the rows the file no longer holds; nothing is ever deleted. Currencies
/// go first, because countries reference them; subdivisions, cities, locales, holidays and address formats follow.
/// </summary>
internal static class GeoSeed
{
    private const string NamePrefix = "name_";
    private const string Codes = "codes";
    private const string Countries = "countries";

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

    private static readonly string[] _holidayHeader =
    [
        "country_code",
        "subdivision_code",
        "date",
        "holiday_key",
        "class",
        "origin",
        "name_en",
        "name_es",
        "name_pt",
    ];

    private static readonly string[] _addressFormatHeader =
    [
        "country_code",
        "postal_code_pattern",
        "postal_code_examples",
        "postal_code_required",
        "required_fields",
        "subdivision_label",
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
        changed += await HolidaysAsync(connection, cancellationToken);
        changed += await AddressFormatsAsync(connection, cancellationToken);
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
            (Countries, Column(rows, 1))
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
                (Countries, countries),
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

    // TODO: generate the 2028 holiday calendar before 2027-12-01 (holidays are seeded per year, one year ahead)
    private static async Task<int> HolidaysAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<string[]> rows = Tsv.Read("holidays.tsv", _holidayHeader);
        (string Name, object Value)[] keys =
        [
            (Countries, Column(rows, 0)),
            ("subdivisions", OptionalColumn(rows, 1)),
            ("dates", Column(rows, 2)),
            ("keys", Column(rows, 3)),
        ];
        int changed = await ExecuteAsync(
            connection,
            """
            INSERT INTO geo.holidays (holiday_id, country_code, subdivision_code, date, holiday_key, class, origin, status)
            SELECT uuidv7(), country, subdivision, day::date, key, class, origin, 'Active'
            FROM unnest(@countries::text[], @subdivisions::text[], @dates::text[], @keys::text[], @classes::text[],
                @origins::text[]) AS seed(country, subdivision, day, key, class, origin)
            ON CONFLICT (country_code, subdivision_code, date, holiday_key) DO UPDATE
            SET class = excluded.class, origin = excluded.origin, status = excluded.status
            WHERE (holidays.class, holidays.origin, holidays.status)
                IS DISTINCT FROM (excluded.class, excluded.origin, excluded.status)
            """,
            cancellationToken,
            [.. keys, ("classes", Column(rows, 4)), ("origins", Column(rows, 5))]
        );
        changed += await HolidayNamesAsync(connection, rows, cancellationToken);
        // Only the calendars (country and year) the file holds: a calendar dropped from the file is kept.
        changed += await ExecuteAsync(
            connection,
            """
            UPDATE geo.holidays h SET status = 'Retired'
            WHERE h.status <> 'Retired'
                AND (h.country_code, extract(year FROM h.date)::int) IN (
                    SELECT DISTINCT country, extract(year FROM day::date)::int
                    FROM unnest(@countries::text[], @dates::text[]) AS calendar(country, day))
                AND NOT EXISTS (
                    SELECT 1 FROM unnest(@countries::text[], @subdivisions::text[], @dates::text[], @keys::text[])
                        AS seed(country, subdivision, day, key)
                    WHERE seed.country = h.country_code AND seed.subdivision IS NOT DISTINCT FROM h.subdivision_code
                        AND seed.day::date = h.date AND seed.key = h.holiday_key)
            """,
            cancellationToken,
            keys
        );
        return changed;
    }

    // A format is updated in place and never retired: a country left out of the file keeps its last format. List
    // cells are comma-separated.
    // TODO: add address formats for another country when Frappé launches there (Argentina only for now)
    private static Task<int> AddressFormatsAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<string[]> rows = Tsv.Read("address-formats.tsv", _addressFormatHeader);
        // A pattern that does not compile fails the seed here, before it is written and loaded by every host.
        foreach (string pattern in OptionalColumn(rows, 1).OfType<string>())
        {
            _ = PostalCodeFormat.Create(pattern, required: false);
        }

        return ExecuteAsync(
            connection,
            """
            INSERT INTO geo.address_formats (country_code, postal_code_pattern, postal_code_examples,
                postal_code_required, required_fields, subdivision_label)
            SELECT country, pattern, string_to_array(examples, ','), required::boolean, string_to_array(fields, ','),
                label
            FROM unnest(@countries::text[], @patterns::text[], @examples::text[], @required::text[], @fields::text[],
                @labels::text[]) AS seed(country, pattern, examples, required, fields, label)
            ON CONFLICT (country_code) DO UPDATE
            SET postal_code_pattern = excluded.postal_code_pattern,
                postal_code_examples = excluded.postal_code_examples,
                postal_code_required = excluded.postal_code_required, required_fields = excluded.required_fields,
                subdivision_label = excluded.subdivision_label
            WHERE (address_formats.postal_code_pattern, address_formats.postal_code_examples,
                    address_formats.postal_code_required, address_formats.required_fields,
                    address_formats.subdivision_label)
                IS DISTINCT FROM (excluded.postal_code_pattern, excluded.postal_code_examples,
                    excluded.postal_code_required, excluded.required_fields, excluded.subdivision_label)
            """,
            cancellationToken,
            (Countries, Column(rows, 0)),
            ("patterns", OptionalColumn(rows, 1)),
            ("examples", Column(rows, 2)),
            ("required", Column(rows, 3)),
            ("fields", Column(rows, 4)),
            ("labels", Column(rows, 5))
        );
    }

    // A holiday has no key of its own in the file, so each name finds its row by the holiday's natural key.
    private static Task<int> HolidayNamesAsync(
        DbConnection connection,
        IReadOnlyList<string[]> rows,
        CancellationToken cancellationToken
    )
    {
        (int Row, string Locale, string Name)[] names =
        [
            .. rows.SelectMany(
                (row, index) =>
                    _holidayHeader
                        .Index()
                        .Where(column =>
                            column.Item.StartsWith(NamePrefix, StringComparison.Ordinal)
                            && row[column.Index].Length > 0
                        )
                        .Select(column =>
                            (index, column.Item[NamePrefix.Length..], row[column.Index])
                        )
            ),
        ];
        return ExecuteAsync(
            connection,
            """
            INSERT INTO geo.holiday_names (holiday_id, locale, name)
            SELECT h.holiday_id, n.locale, n.name
            FROM unnest(@countries::text[], @subdivisions::text[], @dates::text[], @keys::text[], @locales::text[],
                @names::text[]) AS n(country, subdivision, day, key, locale, name)
            JOIN geo.holidays h ON h.country_code = n.country AND h.subdivision_code IS NOT DISTINCT FROM n.subdivision
                AND h.date = n.day::date AND h.holiday_key = n.key
            ON CONFLICT (holiday_id, locale) DO UPDATE SET name = excluded.name
            WHERE holiday_names.name IS DISTINCT FROM excluded.name
            """,
            cancellationToken,
            (Countries, names.Select(name => rows[name.Row][0]).ToArray()),
            (
                "subdivisions",
                names
                    .Select(name => rows[name.Row][1].Length == 0 ? null : rows[name.Row][1])
                    .ToArray()
            ),
            ("dates", names.Select(name => rows[name.Row][2]).ToArray()),
            ("keys", names.Select(name => rows[name.Row][3]).ToArray()),
            ("locales", names.Select(static name => name.Locale).ToArray()),
            ("names", names.Select(static name => name.Name).ToArray())
        );
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
