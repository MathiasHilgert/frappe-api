using Frappe.Geo.Infrastructure.Seed;

namespace Frappe.Geo.UnitTests;

public sealed class TsvTests
{
    private static readonly string[] _header = ["code", "name_en"];

    [Fact]
    public void Rows_are_split_on_tabs_after_the_header()
    {
        using StringReader file = new("code\tname_en\nAR\tArgentina\n");

        IReadOnlyList<string[]> rows = Tsv.Parse(file, "test.tsv", _header);

        rows.ShouldHaveSingleItem().ShouldBe(["AR", "Argentina"]);
    }

    [Fact]
    public void A_file_with_another_header_is_rejected()
    {
        using StringReader file = new("code\tname_es\nAR\tArgentina\n");

        Should.Throw<InvalidDataException>(() => Tsv.Parse(file, "test.tsv", _header));
    }

    [Fact]
    public void A_row_with_another_field_count_is_rejected()
    {
        using StringReader file = new("code\tname_en\nAR\n");

        Should.Throw<InvalidDataException>(() => Tsv.Parse(file, "test.tsv", _header));
    }

    // An empty file would retire every row of its catalogue on the next seed.
    [Theory]
    [InlineData("code\tname_en\n")]
    [InlineData("code\tname_en\n\n")]
    public void A_file_without_rows_is_rejected(string content)
    {
        using StringReader file = new(content);

        Should
            .Throw<InvalidDataException>(() => Tsv.Parse(file, "test.tsv", _header))
            .Message.ShouldContain("test.tsv");
    }
}
