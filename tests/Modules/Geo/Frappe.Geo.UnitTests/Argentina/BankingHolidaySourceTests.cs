using System.Text;
using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Countries.Argentina.Banking;
using Frappe.Geo.Domain.Holidays;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Frappe.Geo.UnitTests.Argentina;

/// <summary>The BCRA's 2026 banking holidays page (real, trimmed) read into Argentina's banking calendar, plus the sector rule.</summary>
public sealed class BankingHolidaySourceTests
{
    private const string Page2026 = "banking-2026";

    private static readonly BankingHolidaySource Source = new(Options.Create(new BankingHolidaysOptions()), new FixedClock(Instant.FromUtc(2026, 11, 13, 12, 0)));

    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bcra", "consulta-feriados-bancarios-2026.html"));

    private static Dictionary<string, ReadOnlyMemory<byte>> Files(Func<string, string>? edit = null) =>
        new(StringComparer.Ordinal) { [Page2026] = Encoding.UTF8.GetBytes(edit?.Invoke(Fixture()) ?? Fixture()) };

    private static CalendarImport Read() => Source.Read(Files()).Value.Calendars.Single();

    [Test]
    public async Task It_downloads_this_year_page_and_next_year_once_it_is_published()
    {
        await Assert.That(Source.Resources.Select(r => (r.Name, r.Location.AbsoluteUri, r.Optional))).IsEquivalentTo(
        [
            ("banking-2026", "https://www.bcra.gob.ar/consulta-feriados-bancarios-2026/", false),
            ("banking-2027", "https://www.bcra.gob.ar/consulta-feriados-bancarios-2027/", true),
        ]);
    }

    [Test]
    public async Task Every_row_of_the_table_is_a_banking_holiday_of_argentina_in_the_banking_scope()
    {
        var calendar = Read();

        await Assert.That((calendar.Jurisdiction.Value, calendar.Year.Value, calendar.Scope.Value)).IsEqualTo(("AR", 2026, "banking"));
        await Assert.That(calendar.Observances.Count(o => o.Date != new LocalDate(2026, 11, 6))).IsEqualTo(20);
        await Assert.That(calendar.Observances.All(o => o.Kind == ObservanceKind.Holiday)).IsTrue();
        var papal = calendar.Observances.Single(o => o.Date == new LocalDate(2026, 11, 10));
        await Assert.That(papal.Name).IsEqualTo("Feriado bancario con motivo de la visita de Su Santidad el Papa León XIV");
        await Assert.That(calendar.Observances.Single(o => o.Date == new LocalDate(2026, 4, 2)).Name)
            .IsEqualTo("Día del Veterano y de los Caídos en la Guerra de Malvinas y Jueves Santo");
        await Assert.That(calendar.Observances.Single(o => o.Date == new LocalDate(2026, 12, 7)).Name).IsEqualTo("Día no laborable con fines turísticos");
    }

    [Test]
    public async Task The_sixth_of_november_is_the_sector_rule_with_its_legal_basis()
    {
        var bankWorkersDay = Read().Observances.Single(o => o.Date == new LocalDate(2026, 11, 6));

        await Assert.That(bankWorkersDay.Name).IsEqualTo("Día del Bancario");
        await Assert.That(bankWorkersDay.Kind).IsEqualTo(ObservanceKind.Holiday);
        await Assert.That(bankWorkersDay.LegalBasis!.Citation).IsEqualTo("CCT 18/75, art. 50");
    }

    [Test]
    public async Task The_bcra_rows_cite_the_comunicaciones_the_page_establishes_its_list_by()
    {
        var papal = Read().Observances.Single(o => o.Date == new LocalDate(2026, 11, 10));

        await Assert.That(papal.LegalBasis!.Citation).IsEqualTo("BCRA, Comunicaciones C 101352, C 102390 y A 8487");
        await Assert.That(papal.LegalBasis.Text!.AbsoluteUri).IsEqualTo("https://www.bcra.gob.ar/archivos/Pdfs/comytexord/C101352.pdf");
    }

    [Test]
    public async Task A_page_that_names_no_comunicacion_still_reads_its_rows_without_a_legal_basis()
    {
        var calendar = Source.Read(Files(html => html.Replace("Listado correspondiente", "Listado", StringComparison.Ordinal))).Value.Calendars.Single();

        await Assert.That(calendar.Observances.Single(o => o.Date == new LocalDate(2026, 11, 10)).LegalBasis).IsNull();
        await Assert.That(calendar.Observances.Single(o => o.Date == new LocalDate(2026, 11, 6)).LegalBasis!.Citation).IsEqualTo("CCT 18/75, art. 50");
    }

    [Test]
    public async Task A_page_without_the_table_or_with_another_shape_refuses_the_download()
    {
        await Assert.That(Source.Read(Files(_ => "<html><body>Mantenimiento</body></html>")).IsSuccess).IsFalse();
        await Assert.That(Source.Read(Files(html => html.Replace("<th>Motivo</th>", "<th>Descripción</th>", StringComparison.Ordinal))).IsSuccess).IsFalse();
        await Assert.That(Source.Read(Files(html => html[..html.IndexOf("<tr>    <td>", StringComparison.Ordinal)] + "</table>")).IsSuccess).IsFalse();
    }

    [Test]
    public async Task A_row_with_an_unreadable_date_or_of_another_year_refuses_the_download()
    {
        await Assert.That(Source.Read(Files(html => html.Replace("<td>25 dic 2026</td>", "<td>25 xyz 2026</td>", StringComparison.Ordinal))).IsSuccess).IsFalse();
        await Assert.That(Source.Read(Files(html => html.Replace("<td>25 dic 2026</td>", "<td>32 dic 2026</td>", StringComparison.Ordinal))).IsSuccess).IsFalse();
        await Assert.That(Source.Read(Files(html => html.Replace("<td>25 dic 2026</td>", "<td>25 dic 2027</td>", StringComparison.Ordinal))).IsSuccess).IsFalse();
        await Assert.That(Source.Read(Files(html => html.Replace("<td>Navidad</td>", "<td> </td>", StringComparison.Ordinal))).IsSuccess).IsFalse();
    }

    [Test]
    public async Task The_sector_rule_is_data_declared_with_its_basis()
    {
        var rule = BankingSectorRules.All.Single();

        await Assert.That((rule.Month, rule.Day, rule.Name)).IsEqualTo((11, 6, "Día del Bancario"));
        await Assert.That(rule.LegalBasis.Citation).IsEqualTo("CCT 18/75, art. 50");
    }

    private sealed class FixedClock(Instant now) : IClock
    {
        public Instant GetCurrentInstant() => now;
    }
}
