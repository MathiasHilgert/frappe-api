using Frappe.Geo.Application.BusinessDays;
using Frappe.Geo.Application.BusinessDays.GetBusinessDay;
using Frappe.Geo.Application.BusinessDays.ListBusinessDays;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Countries.GetCountry;
using Frappe.Geo.Application.Territory;
using Frappe.Geo.Application.Territory.GetSubdivision;
using Frappe.Geo.Contracts.Queries;
using Frappe.Geo.Domain;
using Frappe.Localization;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;
using NodaTime.Text;
using DomainPolicy = Frappe.Geo.Domain.BusinessDays.BusinessDayPolicy;

namespace Frappe.Geo.Application.Queries;

/// <summary>Geo's public queries over its own use cases, translated to the contract's types: nothing is computed twice.</summary>
internal sealed class GeoQueries(
    IQueryHandler<GetCountry, CountryDetail> countries,
    IQueryHandler<GetSubdivision, SubdivisionView> subdivisions,
    IQueryHandler<GetBusinessDay, BusinessDayView> days,
    IQueryHandler<ListBusinessDays, BusinessDaysView> businessDays) : IGeoQueries
{
    private static readonly LanguageTag English = LanguageTag.Create("en").Value;

    public async Task<GeoCountry?> FindCountryAsync(string countryCode, CancellationToken cancellationToken)
    {
        var result = await countries.HandleAsync(new GetCountry(countryCode, English), cancellationToken);
        if (!result.IsSuccess)
        {
            return null;
        }

        var (country, capabilities) = (result.Value.Country, result.Value.Capabilities);
        return new GeoCountry(
            country.Code,
            country.Alpha3,
            country.Numeric,
            country.Name,
            country.CurrencyCodes,
            [.. capabilities.Holidays.Select(h => new GeoHolidayCoverage(h.Scope, h.Years))],
            [.. capabilities.BusinessDayPolicies.Select(p => (BusinessDayPolicy)(int)p)],
            capabilities.TimeZone);
    }

    public async Task<GeoSubdivision?> FindSubdivisionAsync(string subdivisionCode, CancellationToken cancellationToken)
    {
        var result = await subdivisions.HandleAsync(new GetSubdivision(subdivisionCode), cancellationToken);
        return result.IsSuccess ? new GeoSubdivision(result.Value.Code, result.Value.CountryCode, result.Value.Name, result.Value.Category) : null;
    }

    public async Task<BusinessDayAnswer> IsBusinessDayAsync(string jurisdiction, DateOnly date, BusinessDayPolicy policy, CancellationToken cancellationToken)
    {
        var result = await days.HandleAsync(new GetBusinessDay(jurisdiction, Iso(date), NameOf(policy)), cancellationToken);
        return result.IsSuccess
            ? new BusinessDayAnswer(CalendarStatus.Covered, result.Value.Day.IsBusinessDay, result.Value.Day.IsWeekend, KindOf(result.Value))
            : new BusinessDayAnswer(StatusOf(result.Errors), false, false, null);
    }

    public async Task<BusinessDaysAnswer> NextBusinessDaysAsync(string jurisdiction, DateOnly from, int count, BusinessDayPolicy policy, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, IGeoQueries.MaxBusinessDays);
        var result = await businessDays.HandleAsync(
            new ListBusinessDays(jurisdiction, Iso(from), count.ToString(System.Globalization.CultureInfo.InvariantCulture), NameOf(policy)), cancellationToken);
        return result.IsSuccess
            ? new BusinessDaysAnswer(CalendarStatus.Covered, [.. result.Value.Dates.Select(d => d.ToDateOnly())])
            : new BusinessDaysAnswer(StatusOf(result.Errors), []);
    }

    public async Task<DayKindAnswer> GetDayKindAsync(string jurisdiction, DateOnly date, CancellationToken cancellationToken)
    {
        var result = await days.HandleAsync(new GetBusinessDay(jurisdiction, Iso(date), NameOf(BusinessDayPolicy.Labor)), cancellationToken);
        return result.IsSuccess
            ? new DayKindAnswer(CalendarStatus.Covered, KindOf(result.Value), result.Value.Day.IsWeekend)
            : new DayKindAnswer(StatusOf(result.Errors), null, false);
    }

    private static string Iso(DateOnly date) => LocalDatePattern.Iso.Format(LocalDate.FromDateOnly(date));

    private static string NameOf(BusinessDayPolicy policy) => BusinessDayPolicies.NameOf((DomainPolicy)(int)policy);

    private static DayKind? KindOf(BusinessDayView view) => view.Day.Kind is { } kind ? (DayKind)(int)kind : null;

    /// <summary>A gap in Geo's calendars is "not covered"; anything else wrong with the jurisdiction means it does not exist.</summary>
    private static CalendarStatus StatusOf(IReadOnlyList<Error> errors) =>
        errors.Any(e => e.Code is GeoErrorCodes.HolidaysNotCovered or GeoErrorCodes.BusinessDaysNotCovered)
            ? CalendarStatus.NotCovered
            : CalendarStatus.UnknownJurisdiction;
}
