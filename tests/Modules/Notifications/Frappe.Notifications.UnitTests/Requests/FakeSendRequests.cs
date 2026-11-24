using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Requests;

namespace Frappe.Notifications.UnitTests.Requests;

/// <summary>A port that keeps what was staged and never saves.</summary>
internal sealed class FakeSendRequests : ISendRequests
{
    public List<SendRequest> Staged { get; } = [];

    public HashSet<Guid> Known { get; } = [];

    public Task<bool> ExistsAsync(Guid requestId, CancellationToken cancellationToken) => Task.FromResult(Known.Contains(requestId));

    public void Stage(SendRequest request) => Staged.Add(request);
}
