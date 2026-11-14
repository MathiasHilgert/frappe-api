using System.Globalization;
using System.Text.Json;
using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Application.Sync;
using Frappe.Geo.Domain.Holidays;
using Frappe.Geo.Domain.Territory;
using Frappe.Results;
using Microsoft.Extensions.Options;
using NodaTime;
using NodaTime.Text;

namespace Frappe.Geo.Countries.Argentina.Holidays;

/// <summary>
/// Each province's own holidays as Calendarios Nacionales publishes them (calendariosnacionales.com; its terms ask for a
/// visible attribution link, caching and no abusive syncing): this year's file per province, and next year's once it is
/// out. Only the <c>regional</c> rows are read, the province's own observances; the national ones the file repeats come
/// from the government's own calendar. A province with no own observance gets an empty calendar, so a withdrawn one goes.
/// </summary>
internal sealed class ProvincialHolidaySource(IOptions<ProvincialHolidaysOptions> options, IClock clock) : ISnapshotSource<HolidaySnapshot>
{
    private const string Prefix = "provincial-";

    /// <summary>The longest citation a calendar entry stores.</summary>
    private const int MaxCitationLength = 200;

    private static readonly Uri ApiDocumentation = new("https://calendariosnacionales.com/ar/api/");

    /// <summary>
    /// The visible link its terms require ("Debes incluir un enlace funcional a Calendarios Nacionales o a la documentación
    /// de API del país correspondiente"): a constant, never the configurable download location. The terms name no license.
    /// </summary>
    private static readonly Attribution Credit = new("Calendarios Nacionales", ApiDocumentation);

    public string Id => "ar-provincial-holidays";

    private IReadOnlyList<SnapshotResource>? _resources;

    /// <summary>Fixed for the source's life (one run), so a run crossing New Year reads the files it fetched.</summary>
    public IReadOnlyList<SnapshotResource> Resources => _resources ??= ResourcesOf(clock.GetCurrentInstant().InUtc().Year);

    public Result<HolidaySnapshot> Read(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        try
        {
            var calendars = files.Select(file => ReadCalendar(file.Key, file.Value)).ToArray();
            var errors = calendars.Where(c => !c.IsSuccess).SelectMany(c => c.Errors).ToArray();
            return SnapshotRules.Conclude(new HolidaySnapshot(Credit, [.. calendars.Where(c => c.IsSuccess).Select(c => c.Value)]), errors);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or UnparsableValueException)
        {
            return SnapshotRules.Unreadable("provincial holidays", exception.Message);
        }
    }

    private SnapshotResource[] ResourcesOf(int year) =>
        [.. ArgentineProvinces.Codes.Keys.SelectMany(slug => new[] { Resource(year, slug, optional: false), Resource(year + 1, slug, optional: true) })];

    private SnapshotResource Resource(int year, string slug, bool optional)
    {
        var name = year.ToString(CultureInfo.InvariantCulture);
        return new(Prefix + name + "-" + slug, new Uri(options.Value.BaseUrl, $"{name}/provincias/{slug}.json"), optional);
    }

    /// <summary>One province's file, named <c>provincial-{year}-{slug}</c>; it must say it is that province's and year's.</summary>
    private static Result<CalendarImport> ReadCalendar(string name, ReadOnlyMemory<byte> file)
    {
        var calendarYear = int.Parse(name.AsSpan(Prefix.Length, 4), CultureInfo.InvariantCulture);
        var slug = name[(Prefix.Length + 5)..];
        using var document = JsonDocument.Parse(file);
        var root = document.RootElement;
        var region = root.GetProperty("region");
        var fileSlug = region.GetProperty("slug").GetString() ?? string.Empty;
        if (!ArgentineProvinces.Codes.TryGetValue(fileSlug, out var code) || fileSlug != slug)
        {
            throw new FormatException($"Unknown or unexpected province '{fileSlug}' in {name}.");
        }

        if (region.GetProperty("code").GetString() != code || root.GetProperty("year").GetInt32() != calendarYear)
        {
            throw new FormatException($"The file {name} is not {code}'s calendar of {calendarYear}.");
        }

        var year = HolidayYear.Create(calendarYear);
        if (!year.IsSuccess)
        {
            return Result.Failure<CalendarImport>(year.Errors);
        }

        var observances = root.GetProperty("holidays").GetProperty("regional").EnumerateArray()
            .Select(ReadEntry)
            .Where(o => o is not null)
            .Cast<Result<Observance>>()
            .ToArray();
        var errors = observances.Where(o => !o.IsSuccess).SelectMany(o => o.Errors).ToArray();
        return errors.Length > 0
            ? Result.Failure<CalendarImport>(errors)
            : CalendarImport.Create(Jurisdiction.Of(SubdivisionCode.Create(code).Value), year.Value, [.. observances.Select(o => o.Value)]);
    }

    /// <summary>A provincial row as a holiday (the rows carry no kind); <see langword="null"/> for a national one, which is not this source's fact.</summary>
    private static Result<Observance>? ReadEntry(JsonElement entry)
    {
        switch (entry.GetProperty("scope").GetString())
        {
            case "nacional":
                return null;
            case "provincial":
                break;
            default:
                throw new FormatException($"Unknown holiday scope '{entry.GetProperty("scope").GetString()}'.");
        }

        var date = LocalDatePattern.Iso.Parse(entry.GetProperty("date").GetString() ?? string.Empty).Value;
        var movedFrom = entry.TryGetProperty("movedFrom", out var original)
            ? LocalDatePattern.Iso.Parse(original.GetString() ?? string.Empty).Value
            : (LocalDate?)null;
        return Observance.Create(date, entry.GetProperty("name").GetString(), ObservanceKind.Holiday, movedFrom, legalBasis: BasisOf(entry));
    }

    /// <summary>The row's legal source up to its first parenthesis, where the gazette details and quoted text begin.</summary>
    private static LegalBasis? BasisOf(JsonElement entry)
    {
        var source = entry.TryGetProperty("source", out var cited) ? cited.GetString()?.Trim() : null;
        if (string.IsNullOrEmpty(source))
        {
            return null;
        }

        var cut = source.IndexOf(" (", StringComparison.Ordinal);
        var citation = cut > 0 ? source[..cut] : source;
        if (citation.Length > MaxCitationLength)
        {
            throw new FormatException($"Legal source longer than {MaxCitationLength} characters: '{citation[..40]}…'.");
        }

        var url = entry.TryGetProperty("sourceUrl", out var link) && Uri.TryCreate(link.GetString(), UriKind.Absolute, out var parsed) ? parsed : null;
        return new LegalBasis(citation, url);
    }
}
