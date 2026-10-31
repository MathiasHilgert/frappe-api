using System.Text.Json;
using Frappe.Geo.Contracts;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Infrastructure.Catalogues;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Frappe.Geo.IntegrationTests;

// A locality Georef dropped stays as a retired row: references still resolve, lists never show it.
public sealed class RetiredCityTests(GeoFixture geo) : IAsyncLifetime
{
    private const string IndecId = "99999999";

    private static readonly CityId _retired = CityIds.From(IndecId);

    public async ValueTask InitializeAsync()
    {
        await using NpgsqlConnection connection = new(geo.OwnerConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = new(
            """
            INSERT INTO geo.cities (city_id, indec_id, country_code, subdivision_code, name, department_name,
                search_name, search_text, time_zone_id, status)
            VALUES (@id, @indec, 'AR', 'AR-X', 'Zzretirada', NULL, 'zzretirada', 'zzretirada cordoba argentina',
                'America/Argentina/Cordoba', 'Retired')
            ON CONFLICT (city_id) DO NOTHING
            """,
            connection
        );
        command.Parameters.AddWithValue("id", _retired.Value);
        command.Parameters.AddWithValue("indec", IndecId);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_retired_city_resolves_through_the_port_as_retired()
    {
        await using AsyncServiceScope scope = geo.Host.Services.CreateAsyncScope();
        ICityCatalogue cities = scope.ServiceProvider.GetRequiredService<ICityCatalogue>();

        ErrorOr<GeoCity> city = await cities.GetCityAsync(
            _retired,
            TestContext.Current.CancellationToken
        );

        city.Value.ShouldBe(
            new GeoCity(
                _retired,
                "Zzretirada",
                CountryCode.Create("AR").Value,
                SubdivisionCode.Create("AR-X").Value,
                TimeZoneId.Create("America/Argentina/Cordoba").Value,
                ReferenceStatus.Retired
            )
        );
    }

    [Fact]
    public async Task A_retired_city_is_in_no_list()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(
            geo.Host,
            "/v1/geo/cities?q=zzretirada",
            200
        );

        page.GetProperty("items").GetArrayLength().ShouldBe(0);
    }
}
