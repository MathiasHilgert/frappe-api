using System.Buffers.Text;
using System.Text;
using ErrorOr;
using Frappe.Kernel;
using Frappe.Platform.Http.Paging;

namespace Frappe.Platform.UnitTests.Http;

public sealed class CursorCodecTests
{
    private const string Scope = "name:asc|countryCode=AR";

    [Fact]
    public void A_cursor_round_trips_its_keyset_values()
    {
        string cursor = CursorCodec.Encode(["pilar", "06638040"], Scope);

        ErrorOr<IReadOnlyList<string>> keys = CursorCodec.Decode(cursor, Scope);

        keys.Value.ShouldBe(["pilar", "06638040"]);
        cursor.ShouldNotContain("pilar");
    }

    [Fact]
    public void A_tampered_cursor_is_invalid()
    {
        string cursor = CursorCodec.Encode(["pilar"], Scope);
        string tampered = cursor[..^2] + (cursor[^2] == 'A' ? "B" : "A") + cursor[^1];

        ShouldBeInvalid(CursorCodec.Decode(tampered, Scope));
    }

    [Fact]
    public void A_cursor_from_another_sort_or_filter_is_invalid()
    {
        string cursor = CursorCodec.Encode(["pilar"], Scope);

        ShouldBeInvalid(CursorCodec.Decode(cursor, "name:asc|countryCode=UY"));
    }

    [Theory]
    [InlineData("not a cursor!")]
    [InlineData("")]
    [InlineData(null)]
    public void A_cursor_that_does_not_decode_is_invalid(string? cursor)
    {
        ShouldBeInvalid(CursorCodec.Decode(cursor, Scope));
    }

    [Fact]
    public void A_cursor_with_a_null_keyset_value_is_invalid()
    {
        string cursor = CursorCodec.Encode(["pilar"], Scope);
        string json = Encoding
            .UTF8.GetString(Base64Url.DecodeFromChars(cursor))
            .Replace("\"pilar\"", "null", StringComparison.Ordinal);

        ShouldBeInvalid(
            CursorCodec.Decode(Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json)), Scope)
        );
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[1,2]")]
    public void A_cursor_that_is_not_a_scoped_keyset_is_invalid(string json)
    {
        ShouldBeInvalid(
            CursorCodec.Decode(Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json)), Scope)
        );
    }

    private static void ShouldBeInvalid(ErrorOr<IReadOnlyList<string>> result)
    {
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("platform.paging.invalid_cursor");
        result.FirstError.Metadata.ShouldNotBeNull()[Errors.PointerKey].ShouldBe("/cursor");
    }
}
