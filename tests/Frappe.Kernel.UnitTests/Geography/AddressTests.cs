using Frappe.Kernel.Geography;

namespace Frappe.Kernel.UnitTests.Geography;

public sealed class AddressTests
{
    private const int MaxStreetLineLength = 200;

    [Fact]
    public void A_street_line_is_stored_trimmed_with_the_city_and_postal_code()
    {
        var city = CityId.NewId();
        PostalCode postalCode = PostalCode.Create("C1425DQF").Value;

        ErrorOr<Address> result = Address.Create(city, "  Av. Corrientes 1234 ", postalCode);

        result.Value.City.ShouldBe(city);
        result.Value.StreetLine.ShouldBe("Av. Corrientes 1234");
        result.Value.PostalCode.ShouldBe(postalCode);
    }

    [Fact]
    public void An_address_without_a_postal_code_is_accepted()
    {
        var city = CityId.NewId();

        ErrorOr<Address> result = Address.Create(city, "Av. Corrientes 1234", postalCode: null);

        result.Value.PostalCode.ShouldBeNull();
    }

    [Fact]
    public void A_street_line_of_the_maximum_length_is_accepted()
    {
        string streetLine = new('a', MaxStreetLineLength);

        ErrorOr<Address> result = Address.Create(CityId.NewId(), streetLine, postalCode: null);

        result.Value.StreetLine.ShouldBe(streetLine);
    }

    [Fact]
    public void A_street_line_longer_than_the_maximum_is_rejected()
    {
        string streetLine = new('a', MaxStreetLineLength + 1);

        ErrorOr<Address> result = Address.Create(CityId.NewId(), streetLine, postalCode: null);

        result.FirstError.Code.ShouldBe("kernel.address.street_line_invalid");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Av. Corrientes\n1234")]
    [InlineData("Av. Corrientes\u00071234")]
    [InlineData("Calle \u202E4321")]
    [InlineData("a\u200Bb")]
    [InlineData("a\u2028b")]
    [InlineData("a\u2029b")]
    [InlineData("a\uFEFFb")]
    public void An_empty_street_line_or_one_with_control_format_or_separator_characters_is_rejected(
        string? streetLine
    )
    {
        ErrorOr<Address> result = Address.Create(CityId.NewId(), streetLine, postalCode: null);

        result.FirstError.Code.ShouldBe("kernel.address.street_line_invalid");
    }

    // A lone surrogate cannot travel through InlineData: test discovery replaces it with U+FFFD.
    [Fact]
    public void A_street_line_with_a_lone_surrogate_is_rejected()
    {
        const string LoneHighSurrogate = "Calle \uD800 1234";

        ErrorOr<Address> result = Address.Create(
            CityId.NewId(),
            LoneHighSurrogate,
            postalCode: null
        );

        result.FirstError.Code.ShouldBe("kernel.address.street_line_invalid");
    }

    [Fact]
    public void An_address_without_a_city_is_rejected()
    {
        ErrorOr<Address> result = Address.Create(default, "Av. Corrientes 1234", postalCode: null);

        result.FirstError.Code.ShouldBe("kernel.address.city_required");
    }

    [Fact]
    public void A_missing_city_and_an_invalid_street_line_are_reported_together()
    {
        ErrorOr<Address> result = Address.Create(default, "", postalCode: null);

        result
            .Errors.Select(static e => e.Code)
            .ShouldBe(["kernel.address.city_required", "kernel.address.street_line_invalid"]);
    }

    [Fact]
    public void Addresses_with_the_same_parts_are_equal()
    {
        var city = CityId.NewId();
        Address first = Address.Create(city, "Av. Corrientes 1234", postalCode: null).Value;

        Address second = Address.Create(city, " Av. Corrientes 1234 ", postalCode: null).Value;

        first.ShouldBe(second);
    }
}
