using Frappe.Geo.Infrastructure;
using Frappe.Geo.Infrastructure.Addresses;
using Frappe.Geo.Infrastructure.Catalogues;
using Frappe.Geo.Infrastructure.Countries;
using Frappe.Geo.Infrastructure.Currencies;
using Frappe.Geo.Infrastructure.Holidays;
using Frappe.Geo.Infrastructure.Locales;
using Frappe.Geo.Infrastructure.Subdivisions;
using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Frappe.Geo.IntegrationTests;

// A row without names cannot be served, so the catalogue load fails and names the row instead of a bare missing key.
public sealed class UnnamedRowTests(GeoFixture geo)
{
    [Theory]
    [InlineData(
        "currency",
        "INSERT INTO geo.currencies (currency_code, numeric_code, minor_unit, status) "
            + "VALUES ('XQQ', NULL, 2, 'Active')",
        "XQQ"
    )]
    [InlineData(
        "country",
        "INSERT INTO geo.countries (country_code, alpha3, numeric_code, default_currency_code, calling_code, status) "
            + "VALUES ('QQ', 'QQQ', '999', NULL, NULL, 'Active')",
        "QQ"
    )]
    [InlineData(
        "subdivision",
        "INSERT INTO geo.subdivisions (subdivision_code, country_code, status) VALUES ('AR-ZZ', 'AR', 'Active')",
        "AR-ZZ"
    )]
    [InlineData(
        "locale",
        "INSERT INTO geo.locales (locale_tag, language_code, text_direction, endonym, status) "
            + "VALUES ('qu', 'qu', 'ltr', 'Runasimi', 'Active') "
            + "ON CONFLICT (locale_tag) DO UPDATE SET status = 'Active'; "
            + "DELETE FROM geo.locale_names WHERE locale_tag = 'qu'",
        "qu"
    )]
    [InlineData(
        "holiday",
        "INSERT INTO geo.holidays (holiday_id, country_code, date, holiday_key, class, origin, status) "
            + "VALUES ('01900000-0000-7000-8000-000000000001', 'AR', '2026-12-31', 'unnamed', 'Holiday', "
            + "'Fixed', 'Active')",
        "01900000-0000-7000-8000-000000000001"
    )]
    public async Task A_row_without_names_fails_the_load_naming_the_row(
        string catalogue,
        string insert,
        string key
    )
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        // Holidays map dates through NodaTime, as the host's data source does.
        NpgsqlDataSourceBuilder builder = new(geo.OwnerConnectionString);
        builder.UseNodaTime();
        await using NpgsqlDataSource dataSource = builder.Build();
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(
            cancellationToken
        );
        // Never committed, so a host starting meanwhile in another test never sees the unnamed row.
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            cancellationToken
        );
        await using (NpgsqlCommand command = new(insert, connection, transaction))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        DbContextOptionsBuilder<GeoDbContext> options = new();
        options.UseFrappeDatabase(dataSource: null, GeoDbContext.Schema);
        await using GeoDbContext db = new(options.Options);
        db.Database.SetDbConnection(connection);
        await db.Database.UseTransactionAsync(transaction, cancellationToken);
        CountrySnapshot countries = new(new AddressFormatSnapshot());
        Func<Task<LoadedCatalogue>> load = catalogue switch
        {
            "currency" => () => new CurrencySnapshot().LoadAsync(db, cancellationToken),
            "country" => () => countries.LoadAsync(db, cancellationToken),
            "subdivision" => () => new SubdivisionSnapshot().LoadAsync(db, cancellationToken),
            "locale" => () => new LocaleSnapshot().LoadAsync(db, cancellationToken),
            _ => () =>
                new HolidaySnapshot(countries, new SubdivisionSnapshot()).LoadAsync(
                    db,
                    cancellationToken
                ),
        };

        InvalidOperationException failure = await Should.ThrowAsync<InvalidOperationException>(
            load
        );
        await transaction.RollbackAsync(cancellationToken);

        failure.Message.ShouldContain(key);
    }
}
