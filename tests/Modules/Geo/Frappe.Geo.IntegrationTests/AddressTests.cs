using System.Text.Json;
using Frappe.Geo.Contracts;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Infrastructure.Catalogues;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Frappe.Geo.IntegrationTests;

// Other modules validate the addresses they capture through the geo port; geo checks the parts and stores nothing.
public sealed class AddressTests(GeoFixture geo) : IAsyncLifetime
{
    // Villa María, Córdoba: an active locality.
    private static readonly CityId _villaMaria = CityIds.From("14042170");

    // A locality Georef dropped, kept as a retired row.
    private static readonly CityId _retired = CityIds.From("99999998");

    public async ValueTask InitializeAsync()
    {
        await using NpgsqlConnection connection = new(geo.OwnerConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = new(
            """
            INSERT INTO geo.cities (city_id, indec_id, country_code, subdivision_code, name, department_name,
                search_name, search_text, time_zone_id, status)
            VALUES (@id, '99999998', 'AR', 'AR-X', 'Zzdireccionretirada', NULL, 'zzdireccionretirada',
                'zzdireccionretirada cordoba argentina', 'America/Argentina/Cordoba', 'Retired')
            ON CONFLICT (city_id) DO NOTHING
            """,
            connection
        );
        command.Parameters.AddWithValue("id", _retired.Value);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task The_argentine_detail_carries_its_address_format()
    {
        (_, JsonElement country) = await GeoHttp.GetAsync(geo.Host, "/v1/geo/countries/AR", 200);

        JsonElement format = country.GetProperty("addressFormat");
        format
            .GetProperty("postalCodePattern")
            .GetString()
            .ShouldBe(@"([A-HJ-NP-Z])?\d{4}([A-Z]{3})?");
        Strings(format.GetProperty("postalCodeExamples"))
            .ShouldBe(["C1070AAM", "C1000WAM", "B1000TBU", "X5187XAB"]);
        format.GetProperty("postalCodeRequired").GetBoolean().ShouldBeFalse();
        Strings(format.GetProperty("requiredFields")).ShouldBe(["street_line", "city"]);
        format.GetProperty("subdivisionLabel").GetString().ShouldBe("province");
    }

    [Fact]
    public async Task A_country_without_an_address_format_has_a_null_one()
    {
        (_, JsonElement country) = await GeoHttp.GetAsync(geo.Host, "/v1/geo/countries/US", 200);

        country.GetProperty("addressFormat").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_country_list_carries_no_address_format()
    {
        (_, JsonElement page) = await GeoHttp.GetAsync(geo.Host, "/v1/geo/countries", 200);

        GeoHttp
            .Item(page, "countryCode", "AR")
            .TryGetProperty("addressFormat", out _)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData("C1425DQF")]
    [InlineData("c1425dqf")]
    [InlineData("1425")]
    [InlineData("B1629")]
    [InlineData("X5000")]
    [InlineData("1425DQF")]
    public async Task An_argentine_cpa_or_legacy_code_is_valid(string raw)
    {
        ErrorOr<Success> result = await ValidatePostalCodeAsync("AR", raw);

        result.IsError.ShouldBeFalse();
    }

    [Theory]
    [InlineData("ABC")]
    [InlineData("C1425DQFXYZ")]
    [InlineData("I1425")]
    [InlineData("C1425DQ")]
    public async Task An_argentine_code_off_the_pattern_is_invalid(string raw)
    {
        ErrorOr<Success> result = await ValidatePostalCodeAsync("AR", raw);

        result.FirstError.Code.ShouldBe("geo.postal_code.invalid");
    }

    [Fact]
    public async Task A_country_without_an_address_format_accepts_any_postal_code()
    {
        ErrorOr<Success> result = await ValidatePostalCodeAsync("US", "ANY 123-X");

        result.IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task A_postal_code_of_an_unknown_country_is_an_invalid_field()
    {
        ErrorOr<Success> result = await ValidatePostalCodeAsync("ZZ", "1425");

        result.FirstError.Code.ShouldBe("geo.country.not_found");
        result.FirstError.Type.ShouldBe(ErrorType.Validation);
    }

    [Theory]
    [InlineData("Av. Corrientes 1234", "C1425DQF")]
    [InlineData("  Boulevard España  ", null)]
    [InlineData("Ruta 9 km 563", "5900")]
    public async Task An_address_in_an_active_city_with_a_matching_or_missing_postal_code_is_valid(
        string streetLine,
        string? postalCode
    )
    {
        ErrorOr<Success> result = await ValidateAsync(_villaMaria, streetLine, postalCode);

        result.IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task A_street_line_without_a_house_number_is_valid()
    {
        ErrorOr<Success> result = await ValidateAsync(_villaMaria, "Avenida Corrientes", null);

        result.IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task An_address_without_a_city_is_rejected()
    {
        ErrorOr<Success> result = await ValidateAsync(default, "Calle 1", null);

        result.Errors.Select(static error => error.Code).ShouldBe(["geo.address.city_required"]);
    }

    // A consumer passes the errors through for its own body fields, so an unknown city is invalid input, not a 404.
    [Fact]
    public async Task An_unknown_city_is_an_invalid_field()
    {
        ErrorOr<Success> result = await ValidateAsync(CityIds.From("88888888"), "Calle 1", null);

        result.FirstError.Code.ShouldBe("geo.city.not_found");
        result.FirstError.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task A_retired_city_chosen_anew_is_rejected()
    {
        ErrorOr<Success> result = await ValidateAsync(_retired, "Calle 1", null, _villaMaria);

        result.FirstError.Code.ShouldBe("geo.city.retired");
    }

    [Fact]
    public async Task A_retired_city_the_address_already_holds_stays_valid()
    {
        ErrorOr<Success> result = await ValidateAsync(_retired, "Calle 1", null, _retired);

        result.IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task A_postal_code_off_the_city_countrys_pattern_is_rejected_with_a_retired_city()
    {
        ErrorOr<Success> result = await ValidateAsync(_retired, "Calle 1", "C1425DQFXYZ");

        result
            .Errors.Select(static error => error.Code)
            .ShouldBe(["geo.city.retired", "geo.postal_code.invalid"]);
    }

    // The postal code is checked against the country only: a Buenos Aires code is valid for a Córdoba city.
    [Fact]
    public async Task A_postal_code_of_another_province_is_valid()
    {
        ErrorOr<Success> result = await ValidateAsync(_villaMaria, "Calle 1", "C1425DQF");

        result.IsError.ShouldBeFalse();
    }

    private static string?[] Strings(JsonElement array) =>
        [.. array.EnumerateArray().Select(static item => item.GetString())];

    private async Task<ErrorOr<Success>> ValidatePostalCodeAsync(string country, string postalCode)
    {
        await using AsyncServiceScope scope = geo.Host.Services.CreateAsyncScope();
        IAddressValidator addresses = scope.ServiceProvider.GetRequiredService<IAddressValidator>();
        return await addresses.ValidatePostalCode(
            CountryCode.Create(country).Value,
            PostalCode.Create(postalCode).Value,
            TestContext.Current.CancellationToken
        );
    }

    private async Task<ErrorOr<Success>> ValidateAsync(
        CityId city,
        string streetLine,
        string? postalCode,
        CityId? currentCity = null
    )
    {
        await using AsyncServiceScope scope = geo.Host.Services.CreateAsyncScope();
        IAddressValidator addresses = scope.ServiceProvider.GetRequiredService<IAddressValidator>();
        return await addresses.ValidateAsync(
            city,
            StreetLine.Create(streetLine).Value,
            postalCode is null ? null : PostalCode.Create(postalCode).Value,
            currentCity,
            TestContext.Current.CancellationToken
        );
    }
}
