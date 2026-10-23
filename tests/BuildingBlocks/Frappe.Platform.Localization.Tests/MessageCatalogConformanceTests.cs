using Frappe.SharedKernel.Localization;
using Shouldly;

namespace Frappe.Platform.Localization.Tests;

public sealed class MessageCatalogConformanceTests
{
    private static readonly SupportedLocales Supported = new(["en", "es-AR"], "en");

    /// <summary>The check every module copies: one line against its own assembly.</summary>
    [Fact]
    public void SampleCatalogue_IsComplete() =>
        MessageCatalogConformance.Check(typeof(MessageCatalogConformanceTests).Assembly).ShouldBeEmpty();

    [Fact]
    public void MissingKeysMissingLocalesAndBrokenMessages_AreAllReported()
    {
        var catalogues = new Dictionary<Locale, IReadOnlyDictionary<string, string>>
        {
            [Locale.Parse("en")] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["a"] = "A",
                ["b"] = "{count, plural, one {# b} other {# bs}}",
            },
            [Locale.Parse("es-AR")] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["b"] = "{count, plural, one {# b} other {# bs}",
            },
        };

        var problems = MessageCatalogConformance.Check(catalogues, Supported);

        problems.Count.ShouldBe(2);
        problems.ShouldContain("'a' is missing in 'es-AR'.", StringComparer.Ordinal);
        problems.ShouldContain(p => p.StartsWith("'b' in 'es-AR' does not format", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingLocaleCatalogue_IsReported() =>
        MessageCatalogConformance.Check(
                new Dictionary<Locale, IReadOnlyDictionary<string, string>>
                {
                    [Locale.Parse("en")] = new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "A" },
                },
                Supported)
            .ShouldBe(["No catalogue for 'es-AR'."]);

    [Fact]
    public void CodesWithoutEntries_ListsCodesTheCatalogueLacks() =>
        MessageCatalogConformance.CodesWithoutEntries(typeof(MessageCatalogConformanceTests).Assembly,
                ["sample.greeting", "sample.unknown", "sample.unknown"])
            .ShouldBe(["sample.unknown"]);
}
