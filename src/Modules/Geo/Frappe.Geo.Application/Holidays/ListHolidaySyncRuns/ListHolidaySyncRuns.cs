using Frappe.UseCases;

namespace Frappe.Geo.Application.Holidays.ListHolidaySyncRuns;

/// <summary>A holiday source's latest runs (at most <see cref="Limit"/>), newest first.</summary>
public sealed record ListHolidaySyncRuns(string SourceId) : IQuery<IReadOnlyList<SyncRunView>>
{
    public const int Limit = 50;
}
