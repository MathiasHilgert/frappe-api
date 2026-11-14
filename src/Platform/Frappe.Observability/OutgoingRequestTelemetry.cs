using System.Diagnostics;

namespace Frappe.Observability;

/// <summary>
/// What an outgoing HTTP request shows in traces when its own URL carries data that must not leave the process (an
/// identifier of a person's place, a token): the caller marks the request with <see cref="ShowInTelemetryAs"/> and the
/// host's HttpClient instrumentation calls <see cref="Redact"/>, which replaces the span's <c>url.full</c>. The hidden
/// parts read <see cref="Redacted"/>, the token the OpenTelemetry semantic conventions use for redacted query values;
/// those are redacted by the runtime and the instrumentation already, so this is for the path.
/// </summary>
public static class OutgoingRequestTelemetry
{
    /// <summary>The text that stands for a hidden part of a URL.</summary>
    public const string Redacted = "REDACTED";

    /// <summary>The span attribute holding the request's absolute URL.</summary>
    public const string UrlFullTag = "url.full";

    private static readonly HttpRequestOptionsKey<string> TelemetryUrl = new("Frappe.Observability.TelemetryUrl");

    /// <summary>Makes traces show <paramref name="url"/> (absolute, with the sensitive parts redacted) instead of the request's URL.</summary>
    public static HttpRequestMessage ShowInTelemetryAs(this HttpRequestMessage request, string url)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        request.Options.Set(TelemetryUrl, url);
        return request;
    }

    /// <summary>The URL traces show for <paramref name="request"/>, when it was marked; otherwise <see langword="null"/>.</summary>
    public static string? TelemetryUrlOf(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Options.TryGetValue(TelemetryUrl, out var url) ? url : null;
    }

    /// <summary>
    /// Replaces <c>url.full</c> on the request's span with the URL the request was marked with; a request that was not
    /// marked keeps its own. Hosts register it as the HttpClient instrumentation's request enrichment.
    /// </summary>
    public static void Redact(Activity activity, HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (TelemetryUrlOf(request) is { } url)
        {
            activity.SetTag(UrlFullTag, url);
        }
    }
}
