using System.Text.Json;
using Frappe.Geo.Application.Sync;
using Frappe.Geo.Application.Territory;
using Frappe.Results;
using Microsoft.Extensions.Options;

namespace Frappe.Geo.Countries.Argentina.Territory;

/// <summary>
/// Argentina's territory from Georef's full downloads (datos.gob.ar, CC BY 4.0): <c>provincias</c> are the
/// subdivisions (with their ISO 3166-2 code), <c>departamentos</c> level-2 areas, <c>municipios</c> level-3 local
/// governments and <c>localidades</c> the localities. A locality's local government is kept only when
/// <c>municipios</c> lists it: the two files are published apart (189 localities named a municipio missing from it on
/// 2026-11-13), so an unknown one is stored as none instead of refusing the whole download.
/// </summary>
internal sealed class GeorefTerritorySource(IOptions<GeorefOptions> options) : ISnapshotSource<TerritorySnapshot>
{
    private const string Provinces = "provincias";
    private const string Departments = "departamentos";
    private const string Municipalities = "municipios";
    private const string Localities = "localidades";

    public string Id => "ar-georef-territory";

    public IReadOnlyList<SnapshotResource> Resources { get; } =
        [.. new[] { Provinces, Departments, Municipalities, Localities }.Select(name => new SnapshotResource(name, new Uri(options.Value.BaseUrl, name + ".json")))];

    public Result<TerritorySnapshot> Read(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        try
        {
            var provinces = ReadProvinces(files[Provinces]);
            var departments = ReadAreas(files[Departments], Departments, provinces, level: 2, isLocalGovernment: false);
            var governments = ReadAreas(files[Municipalities], Municipalities, provinces, level: 3, isLocalGovernment: true);
            var localities = ReadLocalities(files[Localities], provinces, departments, governments);
            var snapshot = new TerritorySnapshot("AR", [.. provinces.Subdivisions], [.. departments, .. governments], localities);
            return SnapshotRules.Conclude(snapshot, Validate(snapshot));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return SnapshotRules.Unreadable("georef", exception.Message);
        }
    }

    /// <summary>The provinces, and how to turn a Georef province id into its ISO 3166-2 code.</summary>
    private static ProvinceIndex ReadProvinces(ReadOnlyMemory<byte> file)
    {
        var subdivisions = Rows(file, Provinces)
            .Select(p => new SubdivisionImport(Text(p, "iso_id"), Text(p, "id"), Text(p, "nombre"), Text(p, "categoria"), Centroid(p), ArgentinaAliases.Of(Text(p, "iso_id"))))
            .ToArray();
        return new ProvinceIndex(subdivisions, subdivisions.ToDictionary(s => s.SourceId, s => s.Code, StringComparer.Ordinal));
    }

    private static AdministrativeAreaImport[] ReadAreas(ReadOnlyMemory<byte> file, string collection, ProvinceIndex provinces, int level, bool isLocalGovernment) =>
        [.. Rows(file, collection).Select(row => new AdministrativeAreaImport(
            Text(row, "id"), provinces.IsoOf(row), level, Text(row, "categoria"), Text(row, "nombre"), isLocalGovernment, Centroid(row)))];

    /// <summary>A locality keeps a link to an area only when that area was published too (see the class remarks).</summary>
    private static LocalityImport[] ReadLocalities(ReadOnlyMemory<byte> file, ProvinceIndex provinces, AdministrativeAreaImport[] departments, AdministrativeAreaImport[] governments)
    {
        var departmentIds = departments.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var governmentIds = governments.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        return [.. Rows(file, Localities).Select(row => new LocalityImport(
            Text(row, "id"),
            provinces.IsoOf(row),
            KnownOrNone(ReferenceId(row, "departamento"), departmentIds),
            KnownOrNone(ReferenceId(row, "gobierno_local"), governmentIds),
            Text(row, "nombre"),
            Text(row, "categoria"),
            Centroid(row)))];
    }

    private static IEnumerable<Error> Validate(TerritorySnapshot snapshot)
    {
        var codes = snapshot.Subdivisions.Select(s => s.Code).ToHashSet(StringComparer.Ordinal);
        return SnapshotRules.NotEmpty(Provinces, snapshot.Subdivisions)
            .Concat(SnapshotRules.NotEmpty(Localities, snapshot.Localities))
            .Concat(SnapshotRules.UniqueBy(Provinces, snapshot.Subdivisions, s => s.Code))
            .Concat(SnapshotRules.UniqueBy("areas", snapshot.Areas, a => a.Id))
            .Concat(SnapshotRules.UniqueBy(Localities, snapshot.Localities, l => l.Id))
            .Concat(SnapshotRules.Each(Provinces, snapshot.Subdivisions, s => s.Code, s => s.Code.Length is >= 4 and <= 6 && s.Code.StartsWith("AR-", StringComparison.Ordinal)))
            .Concat(SnapshotRules.Each("areas", snapshot.Areas, a => a.Id, a => codes.Contains(a.SubdivisionCode)))
            .Concat(SnapshotRules.Each(Localities, snapshot.Localities, l => l.Id, l => codes.Contains(l.SubdivisionCode)));
    }

    private static string? KnownOrNone(string? id, HashSet<string> known) => id is not null && known.Contains(id) ? id : null;

    private static string? ReferenceId(JsonElement row, string property)
    {
        if (!row.TryGetProperty(property, out var reference) || reference.ValueKind != JsonValueKind.Object || !reference.TryGetProperty("id", out var id))
        {
            return null;
        }

        return id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 } value ? value : null;
    }

    private static Coordinates? Centroid(JsonElement row)
    {
        if (!row.TryGetProperty("centroide", out var centroid) || centroid.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var point = new Coordinates(centroid.GetProperty("lat").GetDouble(), centroid.GetProperty("lon").GetDouble());
        return point.IsValid ? point : null;
    }

    private static string Text(JsonElement row, string property) =>
        row.GetProperty(property).GetString() is { Length: > 0 } text ? text.Trim() : throw new FormatException($"Missing {property}.");

    private static JsonElement[] Rows(ReadOnlyMemory<byte> file, string collection)
    {
        using var document = JsonDocument.Parse(file);
        return [.. document.RootElement.GetProperty(collection).EnumerateArray().Select(row => row.Clone())];
    }

    /// <summary>The provinces read, and their ISO codes by Georef id.</summary>
    private sealed record ProvinceIndex(IReadOnlyList<SubdivisionImport> Subdivisions, Dictionary<string, string> IsoById)
    {
        /// <summary>The ISO code of a row's province, or one no subdivision has, which the validation then reports.</summary>
        public string IsoOf(JsonElement row) => IsoById.TryGetValue(ReferenceId(row, "provincia") ?? string.Empty, out var iso) ? iso : "AR-?";
    }
}
