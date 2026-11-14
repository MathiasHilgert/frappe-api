using Frappe.Geo.Application.BusinessDays;
using Frappe.Geo.Application.Sync;
using Frappe.Geo.Countries.Argentina.Banking;
using Frappe.Geo.Countries.Argentina.Calendar;
using Frappe.Geo.Countries.Argentina.Holidays;
using Frappe.Geo.Countries.Argentina.Territory;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Geo.Countries.Argentina;

/// <summary>
/// Argentina for the hosts to compose: its sources (<c>AddGeoSyncs(geo =&gt; geo.AddArgentina())</c>) and its
/// business-day rules (<see cref="AddArgentinaCalendar"/>), for every host that answers business-day questions.
/// </summary>
public static class ArgentinaGeo
{
    /// <summary>The territory sync, Mondays at 04:00 by default (<c>Geo:Argentina:Territory:Cron</c>).</summary>
    public const string WeeklyTerritorySync = "0 0 4 ? * MON";

    /// <summary>The national holiday sync, daily at 05:00 by default (<c>Geo:Argentina:Holidays:Cron</c>).</summary>
    public const string DailyHolidaySync = "0 0 5 * * ?";

    /// <summary>
    /// The provincial holiday sync, Mondays at 05:30 by default (<c>Geo:Argentina:ProvincialHolidays:Cron</c>): weekly,
    /// as Calendarios Nacionales' terms forbid abusive syncing.
    /// </summary>
    public const string WeeklyProvincialHolidaySync = "0 30 5 ? * MON";

    /// <summary>The BCRA's banking holidays sync, daily at 05:15 by default (<c>Geo:Argentina:BankingHolidays:Cron</c>).</summary>
    public const string DailyBankingHolidaySync = "0 15 5 * * ?";

    /// <summary>Argentina's business-day rules (<see cref="ArgentineCalendarRules"/>).</summary>
    public static IServiceCollection AddArgentinaCalendar(this IServiceCollection services) =>
        services.AddCalendarRules(ArgentineCalendarRules.Rules);

    public static IGeoSources AddArgentina(this IGeoSources geo)
    {
        ArgumentNullException.ThrowIfNull(geo);
        geo.Services.AddOptions<GeorefOptions>().BindConfiguration(GeorefOptions.Section)
            .Validate(o => o.BaseUrl.IsAbsoluteUri && o.BaseUrl.AbsoluteUri.EndsWith('/'), $"Configuration '{GeorefOptions.Section}:BaseUrl' must be an absolute URL ending in '/'.")
            .ValidateOnStart();
        geo.Services.AddOptions<OfficialHolidaysOptions>().BindConfiguration(OfficialHolidaysOptions.Section)
            .Validate(o => o.BaseUrl.IsAbsoluteUri && o.BaseUrl.AbsoluteUri.EndsWith('/'), $"Configuration '{OfficialHolidaysOptions.Section}:BaseUrl' must be an absolute URL ending in '/'.")
            .ValidateOnStart();
        geo.Services.AddOptions<ProvincialHolidaysOptions>().BindConfiguration(ProvincialHolidaysOptions.Section)
            .Validate(o => o.BaseUrl.IsAbsoluteUri && o.BaseUrl.AbsoluteUri.EndsWith('/'), $"Configuration '{ProvincialHolidaysOptions.Section}:BaseUrl' must be an absolute URL ending in '/'.")
            .ValidateOnStart();
        geo.Services.AddOptions<BankingHolidaysOptions>().BindConfiguration(BankingHolidaysOptions.Section)
            .Validate(o => o.BaseUrl.IsAbsoluteUri && o.BaseUrl.AbsoluteUri.EndsWith('/'), $"Configuration '{BankingHolidaysOptions.Section}:BaseUrl' must be an absolute URL ending in '/'.")
            .ValidateOnStart();
        return geo
            .Territory<GeorefTerritorySource>("ar-territory", "Geo:Argentina:Territory", WeeklyTerritorySync)
            .Holidays<OfficialHolidaySource>("ar-holidays", OfficialHolidaysOptions.Section, DailyHolidaySync)
            .Holidays<ProvincialHolidaySource>("ar-provincial-holidays", ProvincialHolidaysOptions.Section, WeeklyProvincialHolidaySync)
            .Holidays<BankingHolidaySource>("ar-banking-holidays", BankingHolidaysOptions.Section, DailyBankingHolidaySync);
    }
}
