using System.Diagnostics.Metrics;

namespace Frappe.Notifications.Senders.Email;

/// <summary>
/// The email provider's request counter on the module's meter (OBS-01): what each call to the provider ended as, so a rise in rejected,
/// transient or idempotency-conflict outcomes can be alerted on. Tags are bounded: the provider is a closed set and the outcome is named.
/// </summary>
public sealed class EmailProviderMetrics
{
    /// <summary>Counter: calls to an email provider, by provider and outcome.</summary>
    public const string RequestsMetric = "frappe.notifications.provider_requests";

    public const string ProviderTag = "provider";

    public const string OutcomeTag = "frappe.outcome";

    private readonly Counter<long> _requests;

    public EmailProviderMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        _requests = meters.Create("Frappe.Notifications").CreateCounter<long>(
            RequestsMetric, unit: "{request}", description: "Calls to the email provider, by provider and outcome.");
    }

    internal void Sent(string provider) => Count(provider, "sent");

    internal void Rejected(string provider) => Count(provider, "rejected");

    internal void Transient(string provider) => Count(provider, "transient");

    internal void Unauthorized(string provider) => Count(provider, "unauthorized");

    internal void IdempotencyConflict(string provider) => Count(provider, "idempotency_conflict");

    private void Count(string provider, string outcome) => _requests.Add(1, new(ProviderTag, provider), new(OutcomeTag, outcome));
}
