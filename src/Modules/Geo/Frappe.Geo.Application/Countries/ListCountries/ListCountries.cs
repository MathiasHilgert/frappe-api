using Frappe.Localization;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Countries.ListCountries;

/// <summary>Every country, named in <paramref name="Language"/> (or the closest language there is).</summary>
public sealed record ListCountries(LanguageTag Language) : IQuery<IReadOnlyList<CountrySummary>>;
