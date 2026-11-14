using Frappe.UseCases;

namespace Frappe.Geo.Application.Holidays.ListHolidaySyncs;

/// <summary>The holiday sources and how their last run went.</summary>
public sealed record ListHolidaySyncs : IQuery<IReadOnlyList<HolidaySyncView>>;
