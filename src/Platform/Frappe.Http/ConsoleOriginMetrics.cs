using System.Diagnostics.Metrics;

namespace Frappe.Http;

/// <summary>
/// The console <c>Origin</c> check's instrument on meter <c>Frappe.Http</c> (<see cref="MeterName"/>),
/// which hosts export through <c>Frappe.*</c>. Created once through <see cref="IMeterFactory"/>. The origin itself is
/// never a tag: whoever sends the request chooses it, so its values are unbounded.
/// </summary>
public sealed class ConsoleOriginMetrics
{
    /// <summary>The meter the HTTP instruments share.</summary>
    public const string MeterName = "Frappe.Http";

    /// <summary>
    /// Counter: unsafe requests carrying a browser credential refused <c>403</c> <see cref="ConsoleOriginCheck.OriginForbiddenCode"/>,
    /// by <see cref="ReasonTag"/> (<c>missing</c> or <c>not_allowed</c>). A sustained count after a deploy is most likely
    /// a console whose origin is missing from <c>Http:Console:Origins</c>.
    /// </summary>
    public const string RefusedMetric = "frappe.http.origin.refused";

    /// <summary>Why the request was refused: <c>missing</c> or <c>not_allowed</c>.</summary>
    public const string ReasonTag = "frappe.http.origin.reason";

    private readonly Counter<long> _refused;

    public ConsoleOriginMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        _refused = meters.Create(MeterName).CreateCounter<long>(
            RefusedMetric,
            unit: "{request}",
            description: "Unsafe requests with a browser credential refused because they came from no console origin.");
    }

    internal void Refused(OriginRefusal refusal) =>
        _refused.Add(1, new KeyValuePair<string, object?>(ReasonTag, refusal switch
        {
            OriginRefusal.Missing => "missing",
            OriginRefusal.NotAllowed => "not_allowed",
            _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "Origin refusal without a tag."),
        }));
}
