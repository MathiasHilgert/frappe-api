using System.Net;
using Frappe.Identity.Application.Sessions;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Owners.VerifyOwnerEmail;

/// <summary>
/// Proves a pending owner's address with the code that was mailed (S6): the owner and their address become active and a session opens, so
/// the owner needs no log-in. A malformed or unknown id, a wrong, expired, used or replaced code and an owner that is not pending all
/// answer <c>identity.code.invalid</c>. It carries a code, so it prints none (DOM-06).
/// </summary>
/// <param name="OwnerId">The id the caller was given when registering, as text: a malformed one is an unknown one.</param>
/// <param name="Code">The six digits, as typed.</param>
/// <param name="UserAgent">The client's user agent, kept with the session.</param>
/// <param name="ClientAddress">The client's address, of which the session keeps only the network prefix.</param>
public sealed record VerifyOwnerEmail(string? OwnerId, string? Code, string? UserAgent, IPAddress? ClientAddress) : ICommand<OpenedSession>
{
    public override string ToString() => $"VerifyOwnerEmail {{ OwnerId = {OwnerId}, Code = *** }}";
}
