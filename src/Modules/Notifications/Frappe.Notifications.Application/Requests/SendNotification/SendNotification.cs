using System.Text.Json;
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
public sealed record SendNotification(Guid RequestId, string Kind, JsonElement Payload) : ICommand<Unit>
{
    public override string ToString() => $"SendNotification {{ RequestId = {RequestId}, Kind = {Kind}, Payload = *** }}";
}
