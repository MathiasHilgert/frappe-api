using Frappe.Geo.Global.Cldr;

namespace Frappe.Geo.UnitTests.Global;

/// <summary>The embedded CLDR snapshot is what the seed promises: the ISO countries, their currencies, named in every common language.</summary>
public sealed class CldrCatalogSeedTests
{
    private static readonly Frappe.Geo.Application.Catalog.CatalogSnapshot Catalog = new CldrCatalogSeed().Load();

    [Test]
    public async Task It_holds_exactly_the_249_ISO_3166_1_countries_and_no_region_or_private_code()
    {
        var codes = Catalog.Countries.Select(c => c.Code).ToArray();

        await Assert.That(codes.Length).IsEqualTo(249);
        await Assert.That(codes).Contains("AR").And.Contains("UY").And.Contains("AQ");
        await Assert.That(codes).DoesNotContain("EU").And.DoesNotContain("XK").And.DoesNotContain("ZZ").And.DoesNotContain("QO");
    }

    [Test]
    public async Task Every_country_is_named_in_English_Spanish_and_Brazilian_Portuguese()
    {
        var unnamed = Catalog.Countries
            .Where(c => !new[] { "en", "es", "pt-BR" }.All(l => c.Names.Any(n => n.Language.Value == l)))
            .Select(c => c.Code);

        await Assert.That(unnamed).IsEmpty();
    }

    [Test]
    public async Task Every_currency_a_country_uses_is_in_the_catalog_and_every_currency_has_a_country()
    {
        var catalog = Catalog.Currencies.Select(c => c.Code).ToHashSet(StringComparer.Ordinal);
        var used = Catalog.Countries.SelectMany(c => c.Currencies).ToHashSet(StringComparer.Ordinal);

        await Assert.That(used.Except(catalog)).IsEmpty();
        await Assert.That(catalog.Except(used)).IsEmpty();
    }

    [Test]
    public async Task A_country_with_two_currencies_keeps_their_order_and_regional_rows_appear_only_where_they_differ()
    {
        var peso = Catalog.Currencies.Single(c => c.Code == "ARS");

        await Assert.That(Catalog.Countries.Single(c => c.Code == "PA").Currencies).IsEquivalentTo(["PAB", "USD"]);
        await Assert.That(peso.Names.Single(n => n.Language.Value == "es-AR").Symbol).IsEqualTo("$");
        await Assert.That(peso.Names.Single(n => n.Language.Value == "es").Symbol).IsEqualTo("ARS");
        await Assert.That(Catalog.Countries.Single(c => c.Code == "AR").Names.Any(n => n.Language.Value == "es-AR")).IsFalse();
    }
}
