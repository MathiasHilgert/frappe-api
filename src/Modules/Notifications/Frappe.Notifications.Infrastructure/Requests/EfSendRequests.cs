using Frappe.Notifications.Application.Ports;
using Frappe.Notifications.Domain.Requests;
using Frappe.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Notifications.Infrastructure.Requests;

/// <summary>Stages accepted requests on the module's context and never saves: the command's unit of work does (UOW-01).</summary>
internal sealed class EfSendRequests(NotificationsDbContext db) : ISendRequests
{
    public Task<bool> ExistsAsync(Guid requestId, CancellationToken cancellationToken) =>
        db.SendRequests.AsNoTracking().AnyAsync(r => r.Id == requestId, cancellationToken);

    public void Stage(SendRequest request) => db.SendRequests.Add(request);
}
