using System.Diagnostics;

namespace Frappe.Api;

/// <summary>
/// What an incoming request shows in traces when its query carries a credential (SEC-09): the value of each name in
/// <see cref="SensitiveQueryNames"/> becomes <see cref="Redacted"/> in the span's <c>url.query</c>, and so in <c>http.target</c> when the
/// instrumentation sets it. The one-click unsubscribe link carries its signed token this way, by RFC 8058; the request logs of the framework
/// are at Warning in the settings, so they print no URL.
/// </summary>
public static class RequestTelemetry
{
    /// <summary>The text that stands for a hidden value, the one the OpenTelemetry semantic conventions use.</summary>
    public const string Redacted = "REDACTED";

    /// <summary>The query names whose values never reach a trace.</summary>
    public static readonly IReadOnlyList<string> SensitiveQueryNames = ["token"];

    private const string UrlQueryTag = "url.query";

    private const string HttpTargetTag = "http.target";

    /// <summary>Replaces the sensitive values of the query on the request's span; hosts register it as the ASP.NET Core request enrichment.</summary>
    public static void RedactQuery(Activity activity, HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Query.Keys.Any(IsSensitive))
        {
            return;
        }

        var query = string.Join('&', request.Query.SelectMany(
            pair => pair.Value.Count == 0 ? [Pair(pair.Key, null)] : pair.Value.Select(value => Pair(pair.Key, value))));
        activity.SetTag(UrlQueryTag, query);
        if (activity.GetTagItem(HttpTargetTag) is not null)
        {
            activity.SetTag(HttpTargetTag, $"{request.Path}?{query}");
        }
    }

    private static string Pair(string name, string? value) =>
        $"{Uri.EscapeDataString(name)}={(IsSensitive(name) ? Redacted : Uri.EscapeDataString(value ?? string.Empty))}";

    private static bool IsSensitive(string name) => SensitiveQueryNames.Contains(name, StringComparer.OrdinalIgnoreCase);
}
