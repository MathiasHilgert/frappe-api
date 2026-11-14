using System.Text;
using Frappe.Geo.Countries.Argentina.Holidays;
using Frappe.Geo.Domain.Holidays;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Frappe.Geo.UnitTests.Argentina;

/// <summary>The government's 2026 calendar file (real, untrimmed) read into Argentina's national calendar.</summary>
public sealed class OfficialHolidaySourceTests
{
    private static readonly OfficialHolidaySource Source = new(Options.Create(new OfficialHolidaysOptions()), new FixedClock(Instant.FromUtc(2026, 11, 13, 12, 0)));

    private static Dictionary<string, ReadOnlyMemory<byte>> Files(Func<string, string>? edit = null)
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "holidays", "holidays-2026-es.json"));
        return new() { ["holidays-2026"] = Encoding.UTF8.GetBytes(edit?.Invoke(json) ?? json) };
    }

    private static IReadOnlyList<Observance> Read() => Source.Read(Files()).Value.Calendars.Single().Observances;

    [Test]
    public async Task It_downloads_this_year_and_next_year_once_it_is_published()
    {
        await Assert.That(Source.Resources.Select(r => (r.Name, r.Location.AbsoluteUri, r.Optional))).IsEquivalentTo(
        [
            ("holidays-2026", "https://www.argentina.gob.ar/sites/default/files/holidays-2026-es.json", false),
            ("holidays-2027", "https://www.argentina.gob.ar/sites/default/files/holidays-2027-es.json", true),
        ]);
    }

    [Test]
    public async Task The_file_is_the_national_calendar_of_its_year_without_next_january_nor_provincial_entries()
    {
        var calendar = Source.Read(Files()).Value.Calendars.Single();

        await Assert.That(calendar.Jurisdiction.Value).IsEqualTo("AR");
        await Assert.That(calendar.Year.Value).IsEqualTo(2026);
        await Assert.That(calendar.Observances.Count).IsEqualTo(32);
        await Assert.That(calendar.Observances.All(o => o.Date.Year == 2026)).IsTrue();
        await Assert.That(calendar.Observances.Any(o => o.Date == new LocalDate(2026, 11, 10))).IsFalse();
    }

    [Test]
    public async Task Each_type_maps_to_its_kind_and_markers_name_the_community()
    {
        var observances = Read();

        await Assert.That(observances.Single(o => o.Name == "Día de la Independencia").Kind).IsEqualTo(ObservanceKind.Holiday);
        await Assert.That(observances.First(o => o.Date == new LocalDate(2026, 3, 23)).Kind).IsEqualTo(ObservanceKind.TouristNonWorkingDay);
        await Assert.That(observances.Single(o => o.Name == "Jueves Santo Festividad Cristiana").Kind).IsEqualTo(ObservanceKind.NonWorkingDay);
        var armenian = observances.Single(o => o.Date == new LocalDate(2026, 4, 24));
        await Assert.That((armenian.Kind, armenian.Community, armenian.Name)).IsEqualTo(
            (ObservanceKind.CommunityNonWorkingDay, (string?)"armenian", "Día de acción por la tolerancia y el respeto entre los pueblos"));
        await Assert.That(observances.Single(o => o.Name == "Día del Perdón").Community).IsEqualTo("jewish");
        await Assert.That(observances.Single(o => o.Name == "Fiesta del Sacrificio").Community).IsEqualTo("islamic");
    }

    [Test]
    public async Task A_moved_holiday_keeps_its_original_date_and_one_that_stayed_has_none()
    {
        var observances = Read();

        var guemes = observances.Single(o => o.Name.StartsWith("Paso a la Inmortalidad del Gral. Don Martín", StringComparison.Ordinal));
        await Assert.That((guemes.Date, guemes.MovedFrom)).IsEqualTo((new LocalDate(2026, 6, 15), (LocalDate?)new LocalDate(2026, 6, 17)));
        await Assert.That(guemes.Name).DoesNotContain("(");
        await Assert.That(observances.Single(o => o.Name == "Paso a la Inmortalidad del Gral. José de San Martín").MovedFrom).IsNull();
        await Assert.That(observances.Any(o => o.Name == "Día de la Raza")).IsTrue();
    }

    [Test]
    public async Task An_extraordinary_holiday_cites_its_decree_and_the_others_their_law()
    {
        var observances = Read();

        var papal = observances.Single(o => o.Date == new LocalDate(2026, 11, 9)).LegalBasis!;
        await Assert.That(papal.Citation).IsEqualTo("Decreto 1103/2026");
        await Assert.That(papal.Text!.AbsoluteUri).IsEqualTo("https://www.argentina.gob.ar/normativa/nacional/norma-430580/texto");
        await Assert.That(observances.Single(o => o.Name == "Navidad").LegalBasis!.Citation).IsEqualTo("Ley 27.399, art. 1");
        await Assert.That(observances.Single(o => o.Name == "Día de la Raza").LegalBasis!.Citation).IsEqualTo("Ley 27.399, art. 6; Decreto 614/2025");
        await Assert.That(observances.Single(o => o.Date == new LocalDate(2026, 4, 24)).LegalBasis!.Citation).IsEqualTo("Ley 26.199");
    }

    [Test]
    public async Task An_unknown_type_or_an_unreadable_file_refuses_the_download()
    {
        await Assert.That(Source.Read(Files(json => json.Replace("\"turistico\"", "\"puente\"", StringComparison.Ordinal))).IsSuccess).IsFalse();
        await Assert.That(Source.Read(Files(json => json.Replace("(17/6)", "(31/6)", StringComparison.Ordinal))).IsSuccess).IsFalse();
        await Assert.That(Source.Read(new Dictionary<string, ReadOnlyMemory<byte>> { ["holidays-2026"] = "<html>"u8.ToArray() }).IsSuccess).IsFalse();
    }

    private sealed class FixedClock(Instant now) : IClock
    {
        public Instant GetCurrentInstant() => now;
    }
}
