using System.Text.Json.Serialization;
using Frappe.Geo.Application.Cities;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Currencies;
using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Application.Locales;
using Frappe.Geo.Application.Subdivisions;
using Frappe.Geo.Application.TimeZones;
using Frappe.Platform.Http.Paging;

namespace Frappe.Geo.Infrastructure;

/// <summary>Source-generated metadata for the geo responses, chained into the HTTP JSON options.</summary>
[JsonSerializable(typeof(PageResponse<TimeZoneDto>))]
[JsonSerializable(typeof(PageResponse<CurrencyDto>))]
[JsonSerializable(typeof(PageResponse<CountryDto>))]
[JsonSerializable(typeof(CountryDto))]
[JsonSerializable(typeof(PageResponse<SubdivisionDto>))]
[JsonSerializable(typeof(PageResponse<CityDto>))]
[JsonSerializable(typeof(PageResponse<LocaleDto>))]
[JsonSerializable(typeof(PageResponse<HolidayDto>))]
internal sealed partial class GeoJsonContext : JsonSerializerContext;
