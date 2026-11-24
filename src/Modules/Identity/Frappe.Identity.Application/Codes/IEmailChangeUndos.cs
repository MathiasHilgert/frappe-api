using Frappe.Identity.Domain.Codes;
using NodaTime;

namespace Frappe.Identity.Application.Codes;

/// <summary>The undo tokens of email changes. It stages and never saves: the command's unit of work does (UOW-01).</summary>
public interface IEmailChangeUndos
{
    /// <summary>The token <paramref name="id"/> whatever its state, tracked for change, or <see langword="null"/> when there is none.</summary>
    Task<EmailChangeUndo?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The live token of <paramref name="principalId"/> for the change of <paramref name="oldAddress"/> to <paramref name="newAddress"/>, tracked.
    /// </summary>
    Task<EmailChangeUndo?> LiveOfPairAsync(Guid principalId, string oldAddress, string newAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Every live token of <paramref name="principalId"/> issued at or after <paramref name="instant"/> (a tie fails closed), tracked for change.
    /// </summary>
    Task<IReadOnlyList<EmailChangeUndo>> LiveIssuedAfterAsync(Guid principalId, Instant instant, CancellationToken cancellationToken);

    /// <summary>Stages <paramref name="undo"/> to be inserted with the unit of work.</summary>
    void Add(EmailChangeUndo undo);
}
