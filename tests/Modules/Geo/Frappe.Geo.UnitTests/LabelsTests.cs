using System.Collections.Frozen;
using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Infrastructure.Catalogues;

namespace Frappe.Geo.UnitTests;

public sealed class LabelsTests
{
    private static readonly Labels _germany = new(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["en"] = "Germany",
            ["es"] = "Alemania",
            ["pt"] = "Alemanha",
        }.ToFrozenDictionary(StringComparer.Ordinal)
    );

    [Theory]
    [InlineData("pt", "Alemanha")]
    [InlineData("es-AR", "Alemania")]
    [InlineData("fr", "Germany")]
    [InlineData("en-GB", "Germany")]
    public void A_name_resolves_along_the_locale_fallback_chain(string locale, string expected)
    {
        string name = _germany.Resolve(Locale.Create(locale).Value);

        name.ShouldBe(expected);
    }
}
