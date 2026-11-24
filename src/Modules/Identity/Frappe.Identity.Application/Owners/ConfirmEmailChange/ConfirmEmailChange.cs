using System.Net;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Owners.ConfirmEmailChange;

/// <summary>
/// Confirms the change of the caller's address with the code mailed to the new one (S15): the address is replaced, proven, and every other
/// session is revoked. A malformed, unknown or another owner's change id, a wrong, expired, used or replaced code, and an address a verified
/// owner holds answer <c>identity.code.invalid</c>. It carries a code, so it prints none (DOM-06).
/// </summary>
/// <param name="PrincipalId">The owner, from the authenticated request.</param>
/// <param name="SessionId">The session of the request, which keeps working.</param>
/// <param name="ChangeId">The id the request answered, as text: a malformed one is an unknown one.</param>
/// <param name="Code">The six digits, as typed.</param>
/// <param name="ClientAddress">The client's address, of which the guess limits keep only the network prefix.</param>
public sealed record ConfirmEmailChange(Guid PrincipalId, Guid SessionId, string? ChangeId, string? Code, IPAddress? ClientAddress)
    : ICommand<Unit>
{
    public override string ToString() => $"ConfirmEmailChange {{ PrincipalId = {PrincipalId}, ChangeId = {ChangeId}, the code is hidden }}";
}
