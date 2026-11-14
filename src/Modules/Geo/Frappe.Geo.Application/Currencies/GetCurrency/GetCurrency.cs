using Frappe.Localization;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Currencies.GetCurrency;

/// <summary>One currency by its ISO 4217 code, as the client wrote it (validated by the handler).</summary>
public sealed record GetCurrency(string CurrencyCode, LanguageTag Language) : IQuery<CurrencySummary>;
