using Frappe.Geo.Infrastructure.Catalogues;
using Microsoft.Net.Http.Headers;

namespace Frappe.Geo.UnitTests;

public sealed class DatasetTagTests
{
    private const string Build = "1.0.0+abc";
    private const string Tzdb = "2025b";

    private static readonly string[] _data =
    [
        "country|AR|ARG|032|ARS|54|Active",
        "country_name|AR|en|Argentina",
    ];

    [Fact]
    public void The_same_dataset_build_and_tzdb_give_the_same_tag()
    {
        EntityTagHeaderValue tag = GeoCatalogueLoader.Tag(_data, Build, Tzdb);

        tag.ShouldBe(GeoCatalogueLoader.Tag(_data, Build, Tzdb));
    }

    [Fact]
    public void A_changed_row_changes_the_tag()
    {
        EntityTagHeaderValue tag = GeoCatalogueLoader.Tag(
            ["country|AR|ARG|032|ARS|54|Active", "country_name|AR|en|Argentine Republic"],
            Build,
            Tzdb
        );

        tag.ShouldNotBe(GeoCatalogueLoader.Tag(_data, Build, Tzdb));
    }

    [Fact]
    public void Another_build_changes_the_tag()
    {
        EntityTagHeaderValue tag = GeoCatalogueLoader.Tag(_data, "1.0.1+def", Tzdb);

        tag.ShouldNotBe(GeoCatalogueLoader.Tag(_data, Build, Tzdb));
    }

    [Fact]
    public void Another_tzdb_changes_the_tag()
    {
        EntityTagHeaderValue tag = GeoCatalogueLoader.Tag(_data, Build, "2025c");

        tag.ShouldNotBe(GeoCatalogueLoader.Tag(_data, Build, Tzdb));
    }
}
