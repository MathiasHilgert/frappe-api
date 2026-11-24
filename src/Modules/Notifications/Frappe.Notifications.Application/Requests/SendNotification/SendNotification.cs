using System.Text.Json;
using Frappe.Access.Contracts;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Notifications.Application.Requests.SendNotification;

/// <summary>
/// Asks Notifications to send one notification of <paramref name="Kind"/> with <paramref name="Payload"/> (S2): the module's own explicit
/// command, which a Development route triggers today and a campaigns API with permissions later. It answers nothing: the send happens in
/// the Worker, after the request is accepted.
/// </summary>
/// <param name="RequestId">Identifies the request across retries; the same id never sends twice (S14).</param>
/// <param name="Kind">The kind's name, as the catalog knows it.</param>
/// <param name="Payload">The kind's payload as JSON; it holds personal data and is never stored in a business table (S13).</param>
public sealed record SendNotification(Guid RequestId, string Kind, JsonElement Payload)
    : ICommand<Unit>, IPublic // proof: none, a Development-only route
{
#pragma warning disable S1135 // SEC-16: the TODO(auth) marks a route that is Development-only until its caller model exists.
    // TODO(auth): SEC-16, the campaigns API decides who may send (a permission of the sender); today only the Development route reaches this.
#pragma warning restore S1135
    public override string ToString() => $"SendNotification {{ RequestId = {RequestId}, Kind = {Kind}, Payload = *** }}";
}
