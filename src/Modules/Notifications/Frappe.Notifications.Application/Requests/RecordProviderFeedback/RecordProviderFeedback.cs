using Frappe.Notifications.Application.Feedback;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Notifications.Application.Requests.RecordProviderFeedback;

/// <summary>
/// Records what a provider reported about a message (S4): the delivery moves as far as its guarded transitions allow, an event that arrives
/// before its delivery is parked, and a complaint, a hard bounce or a provider suppression stops the address. It never fails for an event the
/// rules do not apply (a repeat, a late or out-of-order one): the provider is answered the same and must not retry it.
/// </summary>
/// <param name="Feedback">The event, already authenticated and read by the provider's parser.</param>
public sealed record RecordProviderFeedback(ProviderFeedback Feedback) : ICommand<Unit>;
