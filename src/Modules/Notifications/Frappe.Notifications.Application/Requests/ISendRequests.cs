using Frappe.Notifications.Domain.Requests;

namespace Frappe.Notifications.Application.Requests;

/// <summary>Where accepted send requests are recorded. It only stages: the command's unit of work saves (UOW-01).</summary>
public interface ISendRequests
{
    /// <summary>Whether a request with this identity was already accepted (a retried HTTP request, S2).</summary>
    Task<bool> ExistsAsync(Guid requestId, CancellationToken cancellationToken);

    /// <summary>Stages the request; saving it carries its event to the outbox.</summary>
    void Stage(SendRequest request);
}
