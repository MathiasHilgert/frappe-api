using System.Text.Json;
using Alba;
using Frappe.Geo.Application;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Contracts;
using Frappe.Geo.Contracts.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Frappe.Geo.IntegrationTests;

// A country ISO withdrew stays as a retired row: its detail still answers and shows it retired, lists never show it.
public sealed class RetiredCountryTests(GeoFixture geo) : IAsyncLifetime
{
    private IAlbaHost? _host;

    private IAlbaHost Host =>
        _host ?? throw new InvalidOperationException("The host has not been started.");

    public async ValueTask InitializeAsync()
    {
        // With its name, in one transaction: a host starting meanwhile in another test loads it like any country.
        await ExecuteAsync(
            "INSERT INTO geo.countries (country_code, alpha3, numeric_code, default_currency_code, calling_code, "
                + "status) VALUES ('YU', 'YUG', '891', NULL, NULL, 'Retired') ON CONFLICT DO NOTHING",
            "INSERT INTO geo.country_names (country_code, locale, name) VALUES ('YU', 'en', 'Yugoslavia') "
                + "ON CONFLICT DO NOTHING"
        );
        _host = await geo.StartHostAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        await ExecuteAsync(
            "DELETE FROM geo.country_names WHERE country_code = 'YU'",
            "DELETE FROM geo.countries WHERE country_code = 'YU'"
        );
    }

    [Fact]
    public async Task A_retired_country_is_found_with_its_retired_status()
    {
        (_, JsonElement country) = await GeoHttp.GetAsync(Host, "/v1/geo/countries/YU", 200);

        country.GetProperty("name").GetString().ShouldBe("Yugoslavia");
        country.GetProperty("status").GetString().ShouldBe("retired");
    }

    [Fact]
    public void A_retired_country_is_found_by_the_reader_as_retired()
    {
        ICountryReader countries = Host.Services.GetRequiredService<ICountryReader>();

        Localized<CountryDetailDto>? country = countries.Find(
            CountryCode.Create("YU").Value,
            Locale.English
        );

        country.ShouldNotBeNull().Value.Status.ShouldBe(ReferenceStatus.Retired);
    }

    [Fact]
    public async Task A_retired_country_is_in_no_list()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(Host, "/v1/geo/countries", 200);

        GeoHttp.Codes(page, "countryCode").ShouldNotContain("YU", StringComparer.Ordinal);
    }

    private async Task ExecuteAsync(params string[] statements)
    {
        await using NpgsqlConnection connection = new(geo.OwnerConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            TestContext.Current.CancellationToken
        );
        foreach (string sql in statements)
        {
            await using NpgsqlCommand command = new(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await transaction.CommitAsync(TestContext.Current.CancellationToken);
    }
}
