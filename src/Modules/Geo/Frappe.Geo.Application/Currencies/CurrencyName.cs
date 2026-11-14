using Frappe.Localization;

namespace Frappe.Geo.Application.Currencies;

/// <summary>A currency's name and standard symbol in one language: they always travel together.</summary>
public sealed record CurrencyName(LanguageTag Language, string Name, string Symbol);
