using Frappe.Identity.Application.Codes;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.Infrastructure.Codes;

/// <summary>Stages codes on the module's context and never saves: the command's unit of work does (UOW-01).</summary>
internal sealed class EfOneTimeCodes(IdentityDbContext db) : IOneTimeCodes
{
    public Task<OneTimeCode?> LiveOfAsync(Guid principalId, CodePurpose purpose, CancellationToken cancellationToken) =>
        db.OneTimeCodes.SingleOrDefaultAsync(
            c => c.PrincipalId == principalId && c.Purpose == purpose && c.RedeemedAt == null && c.InvalidatedAt == null, cancellationToken);

    public Task<OneTimeCode?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.OneTimeCodes.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void Add(OneTimeCode code) => db.OneTimeCodes.Add(code);
}
