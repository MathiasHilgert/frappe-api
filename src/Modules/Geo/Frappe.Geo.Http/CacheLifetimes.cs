namespace Frappe.Geo.Http;

/// <summary>How long clients and shared caches may reuse Geo's answers without asking again (<c>max-age</c>).</summary>
internal static class CacheLifetimes
{
    /// <summary>Reference data changes only with a release that brings a new snapshot; the ETag revalidates it afterwards.</summary>
    public static readonly TimeSpan ReferenceData = TimeSpan.FromDays(1);

    /// <summary>Calendars change when a source publishes or someone overrides them; the server evicts its cache in about a second, so five minutes bound how late a client or proxy learns it.</summary>
    public static readonly TimeSpan Calendars = TimeSpan.FromMinutes(5);
}
