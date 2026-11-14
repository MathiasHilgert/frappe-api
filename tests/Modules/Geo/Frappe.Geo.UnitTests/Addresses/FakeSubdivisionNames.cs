using Frappe.Geo.Application.Addresses;

namespace Frappe.Geo.UnitTests.Addresses;

/// <summary>Knows the names given to it, per country; Geo's real one matches names and aliases in its search form.</summary>
internal sealed class FakeSubdivisionNames(params (string Country, string Name, string Code)[] known) : ISubdivisionNames
{
    public Task<string?> FindCodeAsync(string countryCode, IReadOnlyList<string> names, CancellationToken cancellationToken) =>
        Task.FromResult(names.Select(n => known.FirstOrDefault(k => k.Country == countryCode && k.Name == n).Code).FirstOrDefault(c => c is not null));
}
