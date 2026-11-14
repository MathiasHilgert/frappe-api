using Frappe.UseCases;

namespace Frappe.Geo.Application.Holidays.RequestHolidaySync;

/// <summary>Asks for a run of a holiday source now, besides its schedule; the Worker starts it within seconds.</summary>
public sealed record RequestHolidaySync(string SourceId) : ICommand<SyncRequestView>;
