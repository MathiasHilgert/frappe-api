using System.Text;
using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Countries.Argentina.Holidays;
using Frappe.Geo.Domain.Holidays;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Frappe.Geo.UnitTests.Argentina;

/// <summary>Calendarios Nacionales' 2026 provincial files (real, trimmed) read into each province's own calendar.</summary>
public sealed class ProvincialHolidaySourceTests
{
    private static readonly ProvincialHolidaySource Source = new(Options.Create(new ProvincialHolidaysOptions()), new FixedClock(Instant.FromUtc(2026, 11, 13, 12, 0)));

    private static string Fixture(string slug) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "calendarios-nacionales", "2026", "provincias", slug + ".json"));

    private static Dictionary<string, ReadOnlyMemory<byte>> Files(Func<string, string>? edit = null, params string[] slugs) =>
        (slugs.Length == 0 ? ["caba", "cordoba", "buenos-aires", "salta", "chubut"] : slugs)
        .ToDictionary(slug => "provincial-2026-" + slug, slug => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(edit?.Invoke(Fixture(slug)) ?? Fixture(slug)));

    private static Dictionary<string, CalendarImport> Read() =>
        Source.Read(Files()).Value.Calendars.ToDictionary(c => c.Jurisdiction.Value);

    [Test]
    public async Task It_downloads_every_province_this_year_and_next_year_once_it_is_published()
    {
        var resources = Source.Resources;

        await Assert.That(resources.Count).IsEqualTo(48);
        await Assert.That(resources.Where(r => r.Name.EndsWith("-caba", StringComparison.Ordinal)).Select(r => (r.Name, r.Location.AbsoluteUri, r.Optional))).IsEquivalentTo(
        [
            ("provincial-2026-caba", "https://calendariosnacionales.com/ar/v1/2026/provincias/caba.json", false),
            ("provincial-2027-caba", "https://calendariosnacionales.com/ar/v1/2027/provincias/caba.json", true),
        ]);
    }

    [Test]
    public async Task The_slug_table_names_all_twenty_four_jurisdictions_by_their_iso_code()
    {
        await Assert.That(ArgentineProvinces.Codes.Count).IsEqualTo(24);
        await Assert.That(ArgentineProvinces.Codes["caba"]).IsEqualTo("AR-C");
        await Assert.That(ArgentineProvinces.Codes["cordoba"]).IsEqualTo("AR-X");
        await Assert.That(ArgentineProvinces.Codes["buenos-aires"]).IsEqualTo("AR-B");
        await Assert.That(ArgentineProvinces.Codes["tierra-del-fuego"]).IsEqualTo("AR-V");
        await Assert.That(ArgentineProvinces.Codes.Values.Distinct().Count()).IsEqualTo(24);
    }

    [Test]
    public async Task Each_file_is_its_province_calendar_with_only_its_own_observances()
    {
        var calendars = Read();

        await Assert.That(calendars.Keys).IsEquivalentTo(["AR-C", "AR-X", "AR-B", "AR-A", "AR-U"]);
        await Assert.That(calendars.Values.All(c => c.Year.Value == 2026)).IsTrue();
        await Assert.That(calendars.Values.SelectMany(c => c.Observances).Any(o => o.Name == "Año Nuevo")).IsFalse();
        await Assert.That(calendars["AR-U"].Observances.Count).IsEqualTo(0);
        await Assert.That(calendars["AR-A"].Observances.Count).IsEqualTo(3);
    }

    [Test]
    public async Task The_papal_visit_holds_on_its_own_day_in_each_province_with_its_decree()
    {
        var calendars = Read();

        var cordoba = calendars["AR-X"].Observances.Single();
        await Assert.That((cordoba.Date, cordoba.Kind, cordoba.MovedFrom)).IsEqualTo((new LocalDate(2026, 11, 10), ObservanceKind.Holiday, (LocalDate?)null));
        await Assert.That(cordoba.Name).IsEqualTo("Visita oficial de Su Santidad el Papa León XIV");
        await Assert.That(cordoba.LegalBasis!.Citation).IsEqualTo("Decreto DNU 1103/2026, art. 3");
        await Assert.That(cordoba.LegalBasis.Text!.AbsoluteUri).IsEqualTo("https://www.argentina.gob.ar/normativa/nacional/norma-430580/texto");
        await Assert.That(calendars["AR-C"].Observances.Single().Date).IsEqualTo(new LocalDate(2026, 11, 10));
        await Assert.That(calendars["AR-B"].Observances.Single().Date).IsEqualTo(new LocalDate(2026, 11, 11));
        await Assert.That(calendars["AR-A"].Observances.Single(o => o.Date == new LocalDate(2026, 9, 15)).LegalBasis!.Citation).IsEqualTo("Ley provincial 5.032, art. 1 b)");
    }

    [Test]
    public async Task A_saij_document_link_is_kept_since_its_id_is_saij_s_own_format_not_a_guid()
    {
        // SAIJ ids read backwards: 123456789-0abc-defg-230-5000avorpyel is "leyprova 0005-032", Salta's Ley 5.032 (verified 2026-11-13).
        var salta = Read()["AR-A"].Observances.Single(o => o.Date == new LocalDate(2026, 2, 20));

        await Assert.That(salta.LegalBasis!.Text!.AbsoluteUri).IsEqualTo("https://www.saij.gob.ar/view-document?guid=123456789-0abc-defg-230-5000avorpyel");
    }

    [Test]
    public async Task An_unknown_or_mismatched_province_refuses_the_download()
    {
        var unknownSlug = Files(json => json.Replace("\"slug\": \"cordoba\"", "\"slug\": \"atlantida\"", StringComparison.Ordinal), "cordoba");
        var wrongCode = Files(json => json.Replace("\"code\": \"AR-X\"", "\"code\": \"AR-Z\"", StringComparison.Ordinal), "cordoba");
        var wrongYear = Files(json => json.Replace("\"year\": 2026", "\"year\": 2027", StringComparison.Ordinal), "cordoba");

        await Assert.That(Source.Read(unknownSlug).IsSuccess).IsFalse();
        await Assert.That(Source.Read(wrongCode).IsSuccess).IsFalse();
        await Assert.That(Source.Read(wrongYear).IsSuccess).IsFalse();
    }

    [Test]
    public async Task A_national_row_among_the_provincial_ones_is_left_out_and_an_unknown_scope_refuses_the_download()
    {
        var national = Source.Read(Files(json => json.Replace("\"scope\": \"provincial\"", "\"scope\": \"nacional\"", StringComparison.Ordinal), "cordoba"));
        var municipal = Files(json => json.Replace("\"scope\": \"provincial\"", "\"scope\": \"municipal\"", StringComparison.Ordinal), "cordoba");

        await Assert.That(national.Value.Calendars.Single().Observances.Count).IsEqualTo(0);
        await Assert.That(Source.Read(municipal).IsSuccess).IsFalse();
    }

    [Test]
    public async Task An_unreadable_file_or_a_date_outside_its_year_refuses_the_download()
    {
        await Assert.That(Source.Read(new Dictionary<string, ReadOnlyMemory<byte>> { ["provincial-2026-caba"] = "<html>"u8.ToArray() }).IsSuccess).IsFalse();
        await Assert.That(Source.Read(Files(json => json.Replace("\"2026-11-10\"", "\"2026-11-31\"", StringComparison.Ordinal), "caba")).IsSuccess).IsFalse();
        await Assert.That(Source.Read(Files(json => json.Replace("\"date\": \"2026-11-10\"", "\"date\": \"2027-11-10\"", StringComparison.Ordinal), "caba")).IsSuccess).IsFalse();
    }

    private sealed class FixedClock(Instant now) : IClock
    {
        public Instant GetCurrentInstant() => now;
    }
}
