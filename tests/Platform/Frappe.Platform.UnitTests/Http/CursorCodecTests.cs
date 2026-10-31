using ErrorOr;
using Frappe.Kernel;
using Frappe.Platform.Http.Paging;
using Microsoft.AspNetCore.DataProtection;

namespace Frappe.Platform.UnitTests.Http;

public sealed class CursorCodecTests
{
    private const string Scope = "name:asc|countryCode=AR";

    private readonly CursorCodec _codec = new(new EphemeralDataProtectionProvider());

    [Fact]
    public void A_cursor_round_trips_its_keyset_values()
    {
        string cursor = _codec.Encode(["pilar", "06638040"], Scope);

        ErrorOr<IReadOnlyList<string>> keys = _codec.Decode(cursor, Scope);

        keys.Value.ShouldBe(["pilar", "06638040"]);
        cursor.ShouldNotContain("pilar");
    }

    [Fact]
    public void A_tampered_cursor_is_invalid()
    {
        string cursor = _codec.Encode(["pilar"], Scope);
        string tampered = cursor[..^2] + (cursor[^2] == 'A' ? "B" : "A") + cursor[^1];

        ShouldBeInvalid(_codec.Decode(tampered, Scope));
    }

    [Fact]
    public void A_cursor_from_another_sort_or_filter_is_invalid()
    {
        string cursor = _codec.Encode(["pilar"], Scope);

        ShouldBeInvalid(_codec.Decode(cursor, "name:asc|countryCode=UY"));
    }

    [Theory]
    [InlineData("not a cursor!")]
    [InlineData("")]
    [InlineData(null)]
    public void A_cursor_that_does_not_decode_is_invalid(string? cursor)
    {
        ShouldBeInvalid(_codec.Decode(cursor, Scope));
    }

    [Fact]
    public void A_cursor_from_another_key_ring_is_invalid()
    {
        string cursor = new CursorCodec(new EphemeralDataProtectionProvider()).Encode(
            ["pilar"],
            Scope
        );

        ShouldBeInvalid(_codec.Decode(cursor, Scope));
    }

    private static void ShouldBeInvalid(ErrorOr<IReadOnlyList<string>> result)
    {
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("platform.paging.invalid_cursor");
        result.FirstError.Metadata.ShouldNotBeNull()[Errors.PointerKey].ShouldBe("/cursor");
    }
}
