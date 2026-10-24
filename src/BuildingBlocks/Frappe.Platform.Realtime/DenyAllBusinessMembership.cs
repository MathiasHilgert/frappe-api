using System.Security.Claims;
using Frappe.Application.Abstractions.Realtime;

namespace Frappe.Platform.Realtime;

/// <summary>No business module exists yet, so nobody is a member of anything: the stream is closed by default.</summary>
internal sealed class DenyAllBusinessMembership : IBusinessMembership
{
    public Task<bool> IsMemberAsync(ClaimsPrincipal user, Guid businessId, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
