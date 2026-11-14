using Frappe.Localization;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Currencies.ListCountryCurrencies;

/// <summary>The currencies a country uses today, in order of preference (Panama: PAB, then USD).</summary>
public sealed record ListCountryCurrencies(string CountryCode, LanguageTag Language) : IQuery<IReadOnlyList<CurrencySummary>>;
