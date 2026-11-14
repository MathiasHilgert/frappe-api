using Frappe.Localization;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Currencies.ListCurrencies;

/// <summary>Every currency in use today, named in <paramref name="Language"/> (or the closest language there is).</summary>
public sealed record ListCurrencies(LanguageTag Language) : IQuery<IReadOnlyList<CurrencySummary>>;
