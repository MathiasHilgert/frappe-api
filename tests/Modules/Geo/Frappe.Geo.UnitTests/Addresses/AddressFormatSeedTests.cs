using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Domain.Addresses;
using Frappe.Geo.Infrastructure.Seed;

namespace Frappe.Geo.UnitTests.Addresses;

// A bad pattern would seed fine and then fail every host at start, so the checked-in file is checked here.
public sealed class AddressFormatSeedTests
{
    private static readonly string[] _header =
    [
        "country_code",
        "postal_code_pattern",
        "postal_code_examples",
        "postal_code_required",
        "required_fields",
        "subdivision_label",
    ];

    public static TheoryData<string, string, string> Formats =>
        [
            .. Tsv.Read("address-formats.tsv", _header)
                .Select(static row => (row[0], row[1], row[2])),
        ];

    [Theory]
    [MemberData(nameof(Formats))]
    public void Every_seeded_pattern_is_a_whole_regex_that_matches_its_examples(
        string country,
        string pattern,
        string examples
    )
    {
        var format = PostalCodeFormat.Create(pattern.Length == 0 ? null : pattern, required: false);

        examples
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Where(example => format.Validate(PostalCode.Create(example).Value).IsError)
            .ShouldBeEmpty($"{country} examples off its pattern");
    }

    [Fact]
    public void Argentina_has_a_seeded_format()
    {
        Formats
            .Any(static row => string.Equals(row.Data.Item1, "AR", StringComparison.Ordinal))
            .ShouldBeTrue();
    }
}
