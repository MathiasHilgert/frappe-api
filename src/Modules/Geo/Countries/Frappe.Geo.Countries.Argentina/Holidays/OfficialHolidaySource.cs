using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Application.Sync;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Holidays;
using Frappe.Results;
using Microsoft.Extensions.Options;
using NodaTime;
using NodaTime.Text;

namespace Frappe.Geo.Countries.Argentina.Holidays;

/// <summary>
/// Argentina's national calendar as the government publishes it (argentina.gob.ar, CC BY 4.0, from 2025 on): this
/// year's file, and next year's once it is out. Each entry's <c>tipo</c> gives its kind: <c>inamovible</c> and
/// <c>trasladable</c> are holidays (a moved one names its original date, "(17/6)"), <c>turistico</c> a tourist
/// non-working day, <c>no_laborable</c> a non-working day, for a community when marked (a) Armenian, (b) Jewish or
/// (c) Islamic. <c>especial</c> entries hold only for some provinces and are left to the provincial calendars; an
/// entry outside the file's year (next January 1st) belongs to the next file.
/// </summary>
internal sealed partial class OfficialHolidaySource(IOptions<OfficialHolidaysOptions> options, IClock clock) : ISnapshotSource<HolidaySnapshot>
{
    private const string Prefix = "holidays-";
    private static readonly Jurisdiction Argentina = Jurisdiction.Of(CountryCode.Create("AR").Value);
    private static readonly Uri Law27399 = new("https://www.argentina.gob.ar/normativa/nacional/ley-27399-281835");
    private static readonly Uri Portal = new("https://www.argentina.gob.ar/");
    private static readonly Uri Law26199 = new("https://www.argentina.gob.ar/normativa/nacional/ley-26199-124099");

    /// <summary>The credit CC BY 4.0 asks for: a constant, never the configurable download location.</summary>
    private static readonly Attribution Credit = new("argentina.gob.ar", Portal, "CC BY 4.0");

    public string Id => "ar-official-holidays";

    private IReadOnlyList<SnapshotResource>? _resources;

    /// <summary>Fixed for the source's life (one run), so a run crossing New Year reads the files it fetched.</summary>
    public IReadOnlyList<SnapshotResource> Resources => _resources ??= ResourcesOf(clock.GetCurrentInstant().InUtc().Year);

    public Result<HolidaySnapshot> Read(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        try
        {
            var calendars = files.Select(file => ReadCalendar(int.Parse(file.Key[Prefix.Length..], CultureInfo.InvariantCulture), file.Value)).ToArray();
            var errors = calendars.Where(c => !c.IsSuccess).SelectMany(c => c.Errors).ToArray();
            return SnapshotRules.Conclude(new HolidaySnapshot(Credit, [.. calendars.Where(c => c.IsSuccess).Select(c => c.Value)]), errors);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or UnparsableValueException or ArgumentOutOfRangeException)
        {
            return SnapshotRules.Unreadable("holidays", exception.Message);
        }
    }

    private SnapshotResource[] ResourcesOf(int year) => [Resource(year, optional: false), Resource(year + 1, optional: true)];

    private SnapshotResource Resource(int year, bool optional) =>
        new(Prefix + year.ToString(CultureInfo.InvariantCulture), new Uri(options.Value.BaseUrl, $"holidays-{year}-es.json"), optional);

    private static Result<CalendarImport> ReadCalendar(int calendarYear, ReadOnlyMemory<byte> file)
    {
        var year = HolidayYear.Create(calendarYear);
        if (!year.IsSuccess)
        {
            return Result.Failure<CalendarImport>(year.Errors);
        }

        using var document = JsonDocument.Parse(file);
        var root = document.RootElement;
        var decrees = Decrees(root);
        var observances = root.GetProperty("mainEntity").GetProperty("itemListElement").EnumerateArray()
            .Select(entry => ReadEntry(entry.GetProperty("item"), decrees))
            .Where(o => o is not null && (!o.IsSuccess || o.Value.Date.Year == calendarYear))
            .Cast<Result<Observance>>()
            .ToArray();
        var errors = observances.Where(o => !o.IsSuccess).SelectMany(o => o.Errors).ToArray();
        if (errors.Length > 0)
        {
            return Result.Failure<CalendarImport>(errors);
        }

        if (observances.Length == 0)
        {
            return Result.Failure<CalendarImport>([SnapshotRules.Unreadable("holidays-" + calendarYear.ToString(CultureInfo.InvariantCulture), "no national observance")]);
        }

        return CalendarImport.Create(Argentina, year.Value, [.. observances.Select(o => o.Value)]);
    }

    /// <summary>An entry as an observance; <see langword="null"/> for an <c>especial</c> one, which is provincial.</summary>
    private static Result<Observance>? ReadEntry(JsonElement item, IReadOnlyDictionary<string, LegalBasis> decrees)
    {
        var type = item.GetProperty("additionalProperty").GetProperty("value").GetString();
        if (type == "especial")
        {
            return null;
        }

        var date = LocalDatePattern.Iso.Parse(item.GetProperty("startDate").GetString() ?? string.Empty).Value;
        var title = EntryName().Match(Spaces().Replace(item.GetProperty("name").GetString() ?? string.Empty, " ").Trim());
        var name = title.Groups["name"].Value;
        var marker = title.Groups["marker"].Value;
        var (kind, community) = KindOf(type, marker);
        var basis = decrees.GetValueOrDefault(name.ToUpperInvariant()) ?? StandingBasis(type, marker);
        return Observance.Create(date, name, kind, MovedFrom(title, date), community, basis);
    }

    private static (ObservanceKind Kind, string? Community) KindOf(string? type, string marker) => (type, marker) switch
    {
        ("inamovible" or "trasladable", _) => (ObservanceKind.Holiday, null),
        ("turistico", _) => (ObservanceKind.TouristNonWorkingDay, null),
        ("no_laborable", "a") => (ObservanceKind.CommunityNonWorkingDay, "armenian"),
        ("no_laborable", "b") => (ObservanceKind.CommunityNonWorkingDay, "jewish"),
        ("no_laborable", "c") => (ObservanceKind.CommunityNonWorkingDay, "islamic"),
        ("no_laborable", _) => (ObservanceKind.NonWorkingDay, null),
        _ => throw new FormatException($"Unknown holiday type '{type}'."),
    };

    /// <summary>The law behind an entry of its type, as the file's own notes cite it.</summary>
    private static LegalBasis StandingBasis(string? type, string marker) => (type, marker) switch
    {
        ("trasladable", _) => new("Ley 27.399, art. 6; Decreto 614/2025", Law27399),
        ("turistico", _) => new("Ley 27.399, art. 7", Law27399),
        ("no_laborable", "a") => new("Ley 26.199", Law26199),
        ("no_laborable", "c") => new("Ley 27.399, art. 3", Law27399),
        ("no_laborable", _) => new("Ley 27.399, art. 2", Law27399),
        _ => new("Ley 27.399, art. 1", Law27399),
    };

    /// <summary>The original date of a moved holiday, "(17/6)" in its name, in the same year.</summary>
    private static LocalDate? MovedFrom(Match title, LocalDate date) =>
        title.Groups["day"].Success
            ? new LocalDate(date.Year, int.Parse(title.Groups["month"].Value, CultureInfo.InvariantCulture), int.Parse(title.Groups["day"].Value, CultureInfo.InvariantCulture))
            : null;

    /// <summary>
    /// The decrees of extraordinary holidays, by the upper-cased title of their note (<c>subjectOf</c>), which is the
    /// holiday's name: the first link of the note is the decree.
    /// </summary>
    private static Dictionary<string, LegalBasis> Decrees(JsonElement root)
    {
        var decrees = new Dictionary<string, LegalBasis>(StringComparer.Ordinal);
        if (!root.TryGetProperty("subjectOf", out var notes))
        {
            return decrees;
        }

        foreach (var note in notes.EnumerateArray().Where(n => n.GetArrayLength() == 2))
        {
            if (Link().Match(note[1].GetString() ?? string.Empty) is { Success: true } link && Uri.TryCreate(link.Groups["url"].Value, UriKind.Absolute, out var url))
            {
                decrees[(note[0].GetString() ?? string.Empty).Trim().ToUpperInvariant()] = new LegalBasis(link.Groups["citation"].Value, url);
            }
        }

        return decrees;
    }

    [GeneratedRegex(@"^(?<name>.*?)(?:\s*\((?:(?<marker>[abc])|(?<day>\d{1,2})/(?<month>\d{1,2}))\))?$")]
    private static partial Regex EntryName();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\[(?<citation>[^\]]+)\]\((?<url>[^)\s]+)\)")]
    private static partial Regex Link();
}
