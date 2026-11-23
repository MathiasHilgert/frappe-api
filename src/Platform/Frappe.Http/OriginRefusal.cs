namespace Frappe.Http;

/// <summary>Why <see cref="ConsoleOriginCheck"/> refused a request, as <see cref="ConsoleOriginMetrics.RefusedMetric"/> tells them apart.</summary>
public enum OriginRefusal
{
    /// <summary>The request named no <c>Origin</c> at all (<c>missing</c>).</summary>
    Missing,

    /// <summary>It named one that is not a console origin, <c>null</c> included (<c>not_allowed</c>).</summary>
    NotAllowed,
}
