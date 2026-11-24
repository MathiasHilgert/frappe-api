using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Owners.UndoEmailChange;

/// <summary>
/// Restores the address an email change replaced, from the token of the notice sent to it (S15): the old address is back and proven, every
/// session of the owner is revoked and every live code stops working. A malformed, unknown, wrong, expired, used or superseded token, and an old
/// address that another verified owner holds now, answer <c>identity.undo.invalid</c>. It carries a secret, so it prints none (DOM-06).
/// </summary>
/// <param name="Token">The token as the link carried it.</param>
public sealed record UndoEmailChange(string? Token) : ICommand<Unit>
{
    public override string ToString() => "UndoEmailChange { the token is hidden }";
}
