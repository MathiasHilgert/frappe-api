using System.Text.Json;
using Frappe.Kernel.Currencies;
using Frappe.Kernel.Geography;
using Frappe.Kernel.Localization;
using Frappe.Platform.Serialization;

namespace Frappe.Platform.UnitTests.Serialization;

public sealed class KernelValueObjectJsonTests
{
    private static readonly CityId _city = CityId.NewId();

    private static readonly JsonSerializerOptions _options =
        new JsonSerializerOptions().AddKernelValueObjectConverters();

    [Fact]
    public void A_CountryCode_is_written_as_its_JSON_primitive()
    {
        CountryCode value = CountryCode.Create("AR").Value;

        string json = JsonSerializer.Serialize(value, _options);

        json.ShouldBe("\"AR\"");
    }

    [Fact]
    public void A_CountryCode_is_read_from_its_JSON_primitive_through_create()
    {
        CountryCode? value = JsonSerializer.Deserialize<CountryCode>("\"ar\"", _options);

        value.ShouldBe(CountryCode.Create("AR").Value);
    }

    [Fact]
    public void An_invalid_CountryCode_JSON_value_is_rejected()
    {
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<CountryCode>("\"ARG\"", _options)
        );
    }

    [Fact]
    public void A_CurrencyCode_is_written_as_its_JSON_primitive()
    {
        CurrencyCode value = CurrencyCode.Create("ARS").Value;

        string json = JsonSerializer.Serialize(value, _options);

        json.ShouldBe("\"ARS\"");
    }

    [Fact]
    public void A_CurrencyCode_is_read_from_its_JSON_primitive_through_create()
    {
        CurrencyCode? value = JsonSerializer.Deserialize<CurrencyCode>("\"ars\"", _options);

        value.ShouldBe(CurrencyCode.Create("ARS").Value);
    }

    [Fact]
    public void An_invalid_CurrencyCode_JSON_value_is_rejected()
    {
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<CurrencyCode>("\"AR\"", _options)
        );
    }

    [Fact]
    public void A_MinorUnit_is_written_as_its_JSON_primitive()
    {
        MinorUnit value = MinorUnit.Create(2).Value;

        string json = JsonSerializer.Serialize(value, _options);

        json.ShouldBe("2");
    }

    [Fact]
    public void A_MinorUnit_is_read_from_its_JSON_primitive_through_create()
    {
        MinorUnit? value = JsonSerializer.Deserialize<MinorUnit>("2", _options);

        value.ShouldBe(MinorUnit.Create(2).Value);
    }

    [Fact]
    public void An_invalid_MinorUnit_JSON_value_is_rejected()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<MinorUnit>("5", _options));
    }

    [Fact]
    public void A_SubdivisionCode_is_written_as_its_JSON_primitive()
    {
        SubdivisionCode value = SubdivisionCode.Create("AR-X").Value;

        string json = JsonSerializer.Serialize(value, _options);

        json.ShouldBe("\"AR-X\"");
    }

    [Fact]
    public void A_SubdivisionCode_is_read_from_its_JSON_primitive_through_create()
    {
        SubdivisionCode? value = JsonSerializer.Deserialize<SubdivisionCode>("\"ar-x\"", _options);

        value.ShouldBe(SubdivisionCode.Create("AR-X").Value);
    }

    [Fact]
    public void An_invalid_SubdivisionCode_JSON_value_is_rejected()
    {
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<SubdivisionCode>("\"AR\"", _options)
        );
    }

    [Fact]
    public void A_PostalCode_is_written_as_its_JSON_primitive()
    {
        PostalCode value = PostalCode.Create("C1425DQF").Value;

        string json = JsonSerializer.Serialize(value, _options);

        json.ShouldBe("\"C1425DQF\"");
    }

    [Fact]
    public void A_PostalCode_is_read_from_its_JSON_primitive_through_create()
    {
        PostalCode? value = JsonSerializer.Deserialize<PostalCode>("\"c1425dqf\"", _options);

        value.ShouldBe(PostalCode.Create("C1425DQF").Value);
    }

    [Fact]
    public void An_invalid_PostalCode_JSON_value_is_rejected()
    {
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<PostalCode>("\"C1425#\"", _options)
        );
    }

    [Fact]
    public void A_TimeZoneId_is_written_as_its_JSON_primitive()
    {
        TimeZoneId value = TimeZoneId.Create("America/Argentina/Cordoba").Value;

        string json = JsonSerializer.Serialize(value, _options);

        json.ShouldBe("\"America/Argentina/Cordoba\"");
    }

    [Fact]
    public void A_TimeZoneId_is_read_from_its_JSON_primitive_through_create()
    {
        TimeZoneId? value = JsonSerializer.Deserialize<TimeZoneId>(
            "\"america/argentina/cordoba\"",
            _options
        );

        value.ShouldBe(TimeZoneId.Create("America/Argentina/Cordoba").Value);
    }

    [Fact]
    public void An_invalid_TimeZoneId_JSON_value_is_rejected()
    {
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<TimeZoneId>("\"America/Nowhere\"", _options)
        );
    }

    [Fact]
    public void A_Locale_is_written_as_its_JSON_primitive()
    {
        Locale value = Locale.Create("es-AR").Value;

        string json = JsonSerializer.Serialize(value, _options);

        json.ShouldBe("\"es-AR\"");
    }

    [Fact]
    public void A_Locale_is_read_from_its_JSON_primitive_through_create()
    {
        Locale? value = JsonSerializer.Deserialize<Locale>("\"es_ar\"", _options);

        value.ShouldBe(Locale.Create("es-AR").Value);
    }

    [Fact]
    public void An_invalid_Locale_JSON_value_is_rejected()
    {
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<Locale>("\"en-x-private\"", _options)
        );
    }

    [Fact]
    public void A_CityId_is_written_as_its_JSON_primitive()
    {
        CityId value = _city;

        string json = JsonSerializer.Serialize(value, _options);

        json.ShouldBe($"\"{_city.Value}\"");
    }

    [Fact]
    public void A_CityId_is_read_from_its_JSON_primitive_through_create()
    {
        CityId? value = JsonSerializer.Deserialize<CityId>($"\"{_city.Value}\"", _options);

        value.ShouldBe(_city);
    }

    [Fact]
    public void An_invalid_CityId_JSON_value_is_rejected()
    {
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<CityId>("\"3f2504e0-4f89-41d3-9a0c-0305e82c3301\"", _options)
        );
    }

    [Fact]
    public void A_CityId_JSON_null_is_rejected()
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<CityId>("null", _options));
    }

    [Fact]
    public void An_Address_is_written_as_a_street_line_city_id_and_postal_code_object()
    {
        Address address = Address
            .Create(_city, "Av. Corrientes 1234", PostalCode.Create("C1425DQF").Value)
            .Value;

        string json = JsonSerializer.Serialize(address, _options);

        json.ShouldBe(
            $$"""{"streetLine":"Av. Corrientes 1234","cityId":"{{_city.Value}}","postalCode":"C1425DQF"}"""
        );
    }

    [Fact]
    public void An_Address_without_a_postal_code_omits_it_from_the_JSON_object()
    {
        Address address = Address.Create(_city, "Av. Corrientes 1234", postalCode: null).Value;

        string json = JsonSerializer.Serialize(address, _options);

        json.ShouldBe($$"""{"streetLine":"Av. Corrientes 1234","cityId":"{{_city.Value}}"}""");
    }

    [Fact]
    public void An_Address_is_read_from_its_JSON_object_through_create()
    {
        string json =
            $$"""{"cityId":"{{_city.Value}}","streetLine":" Av. Corrientes 1234 ","postalCode":"c1425dqf"}""";

        Address? address = JsonSerializer.Deserialize<Address>(json, _options);

        address.ShouldBe(
            Address.Create(_city, "Av. Corrientes 1234", PostalCode.Create("C1425DQF").Value).Value
        );
    }

    [Theory]
    [InlineData("""{"streetLine":"Av. Corrientes 1234"}""")]
    [InlineData("""{"streetLine":"","cityId":"0190a6b2-1234-7abc-8def-123456789abc"}""")]
    [InlineData(
        """{"streetLine":"Av. Corrientes 1234","cityId":"3f2504e0-4f89-41d3-9a0c-0305e82c3301"}"""
    )]
    [InlineData(
        """{"streetLine":"Av. Corrientes 1234","cityId":"0190a6b2-1234-7abc-8def-123456789abc","postalCode":"C1425#"}"""
    )]
    [InlineData(
        """{"streetLine":"Av. Corrientes 1234","cityId":"0190a6b2-1234-7abc-8def-123456789abc","floor":"3"}"""
    )]
    [InlineData("\"Av. Corrientes 1234\"")]
    public void An_invalid_Address_JSON_value_is_rejected(string json)
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Address>(json, _options));
    }
}
