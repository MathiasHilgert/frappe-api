using System.Text;
using Frappe.Geo.Application.Sync;
using Frappe.Geo.Countries.Argentina.Territory;
using Microsoft.Extensions.Options;

namespace Frappe.Geo.UnitTests.Argentina;

/// <summary>Georef's downloads (real rows, trimmed) read into the canonical territory.</summary>
public sealed class GeorefTerritorySourceTests
{
    private static readonly GeorefTerritorySource Source = new(Options.Create(new GeorefOptions()));

    private static Dictionary<string, ReadOnlyMemory<byte>> Files(Func<string, string, string>? edit = null) =>
        Source.Resources.ToDictionary(
            r => r.Name,
            r =>
            {
                var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "georef", r.Name + ".json"));
                return (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(edit?.Invoke(r.Name, json) ?? json);
            });

    [Test]
    public async Task It_downloads_the_four_full_files_from_the_configured_address()
    {
        await Assert.That(Source.Resources.Select(r => r.Location.AbsoluteUri)).IsEquivalentTo(
        [
            "https://infra.datos.gob.ar/georef/provincias.json", "https://infra.datos.gob.ar/georef/departamentos.json",
            "https://infra.datos.gob.ar/georef/municipios.json", "https://infra.datos.gob.ar/georef/localidades.json",
        ]);
    }

    [Test]
    public async Task Provinces_are_subdivisions_departments_level_2_and_municipalities_level_3_local_governments()
    {
        var snapshot = Source.Read(Files()).Value;

        var buenosAires = snapshot.Subdivisions.Single(s => s.Code == "AR-C");
        await Assert.That(snapshot.CountryCode).IsEqualTo("AR");
        await Assert.That(snapshot.Subdivisions.Select(s => s.Code)).IsEquivalentTo(["AR-C", "AR-H", "AR-X"]);
        await Assert.That(buenosAires.Category).IsEqualTo("Ciudad Autónoma");
        await Assert.That(buenosAires.Aliases).Contains("CABA");
        await Assert.That(snapshot.Areas.Where(a => a.Level == 2).All(a => !a.IsLocalGovernment)).IsTrue();
        var cordobaCity = snapshot.Areas.Single(a => a.Id == "140077");
        await Assert.That(cordobaCity.Level).IsEqualTo(3);
        await Assert.That(cordobaCity.IsLocalGovernment).IsTrue();
        await Assert.That(cordobaCity.SubdivisionCode).IsEqualTo("AR-X");
    }

    [Test]
    public async Task A_locality_links_its_areas_and_an_unpublished_local_government_becomes_none()
    {
        var snapshot = Source.Read(Files()).Value;

        var cordoba = snapshot.Localities.Single(l => l.Id == "14014010");
        var espinillo = snapshot.Localities.Single(l => l.Id == "22063010");
        await Assert.That((cordoba.SubdivisionCode, cordoba.AreaId, cordoba.LocalGovernmentId)).IsEqualTo(("AR-X", "14014", "140077"));
        await Assert.That(espinillo.LocalGovernmentId).IsNull();
        await Assert.That(espinillo.AreaId).IsEqualTo("22063");
    }

    [Test]
    public async Task An_empty_file_a_duplicate_or_a_locality_of_an_unknown_province_refuses_the_whole_download()
    {
        var empty = Source.Read(Files((name, json) => name == "localidades" ? """{ "localidades": [] }""" : json));
        var unknownProvince = Source.Read(Files((name, json) => name == "localidades" ? json.Replace("\"id\": \"22\"", "\"id\": \"99\"", StringComparison.Ordinal) : json));

        await Assert.That(empty.Errors.Select(e => e.Code)).Contains(SnapshotRules.EmptyCode);
        await Assert.That(unknownProvince.Errors.Select(e => e.Code)).Contains(SnapshotRules.InvalidRowCode);
    }

    [Test]
    public async Task A_file_that_is_not_Georef_is_unreadable_not_a_crash()
    {
        var result = Source.Read(Files((name, json) => name == "provincias" ? "<html>maintenance</html>" : json));

        await Assert.That(result.Errors.Single().Code).IsEqualTo(SnapshotRules.UnreadableCode);
    }
}
