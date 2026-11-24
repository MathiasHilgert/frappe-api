using System.Net;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Owners.ResetPassword;

/// <summary>
/// Sets a new password with the code that was mailed (S11) and revokes every session of the owner. A malformed, unknown or made-up reset id,
/// and a wrong, expired, used or replaced code, answer <c>identity.code.invalid</c>. It carries a code and a password, so it prints neither
/// (DOM-06).
/// </summary>
/// <param name="ResetId">The id the reset request answered, as text: a malformed one is an unknown one.</param>
/// <param name="Code">The six digits, as typed.</param>
/// <param name="NewPassword">The password the owner wants, as typed.</param>
/// <param name="ClientAddress">The client's address, of which the guess limits keep only the network prefix.</param>
public sealed record ResetPassword(string? ResetId, string? Code, string? NewPassword, IPAddress? ClientAddress) : ICommand<Unit>
{
    public override string ToString() => $"ResetPassword {{ ResetId = {ResetId}, the code and the password are hidden }}";
}
