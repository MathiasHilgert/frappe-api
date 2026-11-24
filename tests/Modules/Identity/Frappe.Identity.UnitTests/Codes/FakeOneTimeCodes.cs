using Frappe.Identity.Application.Codes;
using Frappe.Identity.Domain.Codes;

namespace Frappe.Identity.UnitTests.Codes;

/// <summary>An in-memory repository: what a unit of work would write is what was added or changed.</summary>
internal sealed class FakeOneTimeCodes : IOneTimeCodes
{
    public List<OneTimeCode> Stored { get; } = [];

    public Task<OneTimeCode?> LiveOfAsync(Guid principalId, CodePurpose purpose, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(c => c.PrincipalId == principalId && c.Purpose == purpose && c.HoldsLiveSlot));

    public Task<IReadOnlyList<OneTimeCode>> LiveOfAsync(Guid principalId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OneTimeCode>>([.. Stored.Where(c => c.PrincipalId == principalId && c.HoldsLiveSlot)]);

    public Task<OneTimeCode?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(c => c.Id == id));

    public void Add(OneTimeCode code) => Stored.Add(code);
}
