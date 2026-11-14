using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Application.Sync;
using Frappe.Geo.Domain.Countries;
using Frappe.Geo.Domain.Holidays;
using Frappe.Results;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Frappe.Geo.Countries.Argentina.Banking;

/// <summary>
/// The days Argentina's banks do not open, as Argentina's <c>banking</c> calendar: the BCRA's yearly list of banking
/// holidays (its page <c>consulta-feriados-bancarios-{year}/</c>, the table the Comunicaciones "C" set), this year's and
/// next year's once it is out, plus the sector's own rules (<see cref="BankingSectorRules"/>). Every row is stored as a
/// holiday, as the BCRA lists them all alike. The list repeats the national holidays: it is stored whole, as one source's
/// fact, rather than as its difference with the general calendar, which a later change of the latter would leave stale.
/// Each row's legal basis is the Comunicaciones the page says the year's list follows (it names them for the whole list,
/// not per row), linked to the first one's text; a page that names none leaves the rows without a basis rather than
/// guessing one. Where the general calendar names the same day, business-day answers show the general observance (its
/// kind and basis), not this row (<c>BusinessCalendar</c>, with business days).
/// </summary>
internal sealed class BankingHolidaySource(IOptions<BankingHolidaysOptions> options, IClock clock) : ISnapshotSource<HolidaySnapshot>
{
    private const string Prefix = "banking-";

    /// <summary>The scope of the calendar this source fills.</summary>
    public static CalendarScope Scope { get; } = CalendarScope.Create("banking").Value;

    private static readonly Jurisdiction Argentina = Jurisdiction.Of(CountryCode.Create("AR").Value);
    private static readonly Uri BcraSite = new("https://www.bcra.gob.ar/");

    /// <summary>The BCRA's pages publish no license; the credit names the publisher and links to it.</summary>
    private static readonly Attribution Credit = new("Banco Central de la República Argentina", BcraSite, null);

    private IReadOnlyList<SnapshotResource>? _resources;

    public string Id => "ar-banking-holidays";

    /// <summary>Fixed for the source's life (one run), so a run crossing New Year reads the pages it fetched.</summary>
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
        catch (Exception exception) when (exception is FormatException or RegexMatchTimeoutException)
        {
            return SnapshotRules.Unreadable("banking holidays", exception.Message);
        }
    }

    private SnapshotResource[] ResourcesOf(int year) => [Resource(year, optional: false), Resource(year + 1, optional: true)];

    private SnapshotResource Resource(int year, bool optional)
    {
        var name = year.ToString(CultureInfo.InvariantCulture);
        return new(Prefix + name, new Uri(options.Value.BaseUrl, $"consulta-feriados-bancarios-{name}/"), optional);
    }

    /// <summary><c>BCRA, Comunicación C 101352</c>, or <c>BCRA, Comunicaciones C 101352, C 102390 y A 8487</c>, linked to the first.</summary>
    private static LegalBasis? BasisOf(IReadOnlyList<(string Code, string Link)> comunicaciones)
    {
        if (comunicaciones.Count == 0)
        {
            return null;
        }

        var codes = comunicaciones.Select(c => c.Code).Distinct(StringComparer.Ordinal).ToArray();
        var citation = codes.Length == 1
            ? $"BCRA, Comunicación {codes[0]}"
            : $"BCRA, Comunicaciones {string.Join(", ", codes[..^1])} y {codes[^1]}";
        var text = Uri.TryCreate(BcraSite, comunicaciones[0].Link, out var link) ? link : null;
        return citation.Length <= LegalBasis.MaxCitationLength ? new LegalBasis(citation, text) : null;
    }

    /// <summary>One year's page, named <c>banking-{year}</c>: every row must fall in that year.</summary>
    private static Result<CalendarImport> ReadCalendar(string name, ReadOnlyMemory<byte> file)
    {
        var calendarYear = int.Parse(name.AsSpan(Prefix.Length), CultureInfo.InvariantCulture);
        var year = HolidayYear.Create(calendarYear);
        if (!year.IsSuccess)
        {
            return Result.Failure<CalendarImport>(year.Errors);
        }

        var html = Encoding.UTF8.GetString(file.Span);
        var rows = BcraHolidayTable.Read(html);
        var basis = BasisOf(BcraHolidayTable.Comunicaciones(html));
        var listed = rows.Select(row => Observance.Create(row.Date, row.Reason, ObservanceKind.Holiday, legalBasis: basis)).ToList();
        var dates = rows.Select(row => row.Date).ToHashSet();
        listed.AddRange(BankingSectorRules.All
            .Select(rule => new LocalDate(calendarYear, rule.Month, rule.Day))
            .Zip(BankingSectorRules.All)
            .Where(pair => !dates.Contains(pair.First))
            .Select(pair => Observance.Create(pair.First, pair.Second.Name, ObservanceKind.Holiday, legalBasis: pair.Second.LegalBasis)));
        var errors = listed.Where(o => !o.IsSuccess).SelectMany(o => o.Errors).ToArray();
        return errors.Length > 0
            ? Result.Failure<CalendarImport>(errors)
            : CalendarImport.Create(Argentina, year.Value, Scope, [.. listed.Select(o => o.Value)]);
    }
}
