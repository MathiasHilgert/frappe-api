using Frappe.Geo.Application.Countries;
using Frappe.Geo.Domain.Countries;

namespace Frappe.Geo.UnitTests.Addresses;

/// <summary>The countries Geo knows, by ISO 3166-1 alpha-2 code; nothing else.</summary>
internal sealed class FakeCountryCatalog(params string[] codes) : ICountryCatalog
{
    public Task<IReadOnlyList<CountryView>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CountryView>>([.. codes.Order(StringComparer.Ordinal).Select(View)]);

    public Task<CountryView?> FindAsync(CountryCode code, CancellationToken cancellationToken) =>
        Task.FromResult(codes.Contains(code.Value, StringComparer.Ordinal) ? View(code.Value) : null);

    private static CountryView View(string code) => new(code, code + "X", "000", [], []);
}
