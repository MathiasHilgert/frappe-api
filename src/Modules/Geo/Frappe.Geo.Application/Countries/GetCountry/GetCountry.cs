using Frappe.Localization;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Countries.GetCountry;

/// <summary>One country by its ISO 3166-1 alpha-2 code, as the client wrote it (validated by the handler).</summary>
public sealed record GetCountry(string CountryCode, LanguageTag Language) : IQuery<CountrySummary>;
