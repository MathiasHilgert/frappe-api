using Frappe.Identity.Domain.Codes;

namespace Frappe.Identity.Application.Codes;

/// <summary>The one-time codes of principals. It stages and never saves: the command's unit of work does (UOW-01).</summary>
public interface IOneTimeCodes
{
    /// <summary>
    /// The code of <paramref name="purpose"/> that holds <paramref name="principalId"/>'s live slot (neither redeemed nor invalidated), tracked
    /// for change, or <see langword="null"/>. It does not look at the clock: an expired code nobody redeemed still holds the slot until a newer
    /// one invalidates it, and the unique index counts it.
    /// </summary>
    Task<OneTimeCode?> LiveOfAsync(Guid principalId, CodePurpose purpose, CancellationToken cancellationToken);

    /// <summary>The code <paramref name="id"/> whatever its state, tracked for change, or <see langword="null"/> when there is none.</summary>
    Task<OneTimeCode?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Stages <paramref name="code"/> to be inserted with the unit of work.</summary>
    void Add(OneTimeCode code);
}
