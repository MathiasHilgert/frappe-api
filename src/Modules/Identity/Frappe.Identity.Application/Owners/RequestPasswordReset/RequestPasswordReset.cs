using Frappe.UseCases;

namespace Frappe.Identity.Application.Owners.RequestPasswordReset;

/// <summary>
/// Asks for a password reset code for an address (S11). It always succeeds with a reset id, whatever the address is: a code is mailed only to
/// the verified owner who has it, and every other address (unknown, malformed, pending, not an owner's, over its send budget) is answered a
/// made-up id, so the answer reveals nothing.
/// </summary>
/// <param name="Email">The address, as typed.</param>
public sealed record RequestPasswordReset(string? Email) : ICommand<PasswordResetRequested>
{
    public override string ToString() => "RequestPasswordReset { the content is hidden }";
}
