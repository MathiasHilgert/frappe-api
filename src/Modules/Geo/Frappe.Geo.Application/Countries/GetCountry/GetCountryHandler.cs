using Frappe.Geo.Application.BusinessDays;
using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Domain.Countries;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Countries.GetCountry;

internal sealed class GetCountryHandler(ICountryCatalog catalog, IHolidayCalendars calendars, ICalendarRulesCatalog rules)
    : IQueryHandler<GetCountry, CountryDetail>
{
    public async Task<Result<CountryDetail>> HandleAsync(GetCountry query, CancellationToken cancellationToken)
    {
        var code = CountryCode.Create(query.CountryCode);
        if (!code.IsSuccess)
        {
            return Result.Failure<CountryDetail>(code.Errors);
        }

        if (await catalog.FindAsync(code.Value, cancellationToken) is not { } country)
        {
            return GeoErrors.CountryNotFound(code.Value.Value);
        }

        var coverage = await calendars.CoverageAsync(code.Value, cancellationToken);
        return new CountryDetail(
            CountrySummary.Serve(country, query.Language),
            CountryCapabilities.Of(coverage.Select(c => (c.Scope.Value, c.Year)), rules.Find(code.Value)));
    }
}
