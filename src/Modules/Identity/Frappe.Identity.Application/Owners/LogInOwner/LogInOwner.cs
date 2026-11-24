using System.Net;
using Frappe.Access.Contracts;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Owners.LogInOwner;

/// <summary>
/// Logs an owner in with an address and a password (S7). A right password opens a session; for an owner whose address is not verified it sends
/// a new code instead and says so. Every other case is the one <c>identity.credentials.invalid</c>, never faster than
/// <see cref="Credentials.PasswordCheck.RefusalFloor"/>; an address that tries too often is throttled. It carries a password, so it prints none
/// (DOM-06).
/// </summary>
/// <param name="Email">The address, as typed.</param>
/// <param name="Password">The password, as typed.</param>
/// <param name="UserAgent">The client's user agent, kept with the session.</param>
/// <param name="ClientAddress">The client's address, of which the session keeps only the network prefix.</param>
public sealed record LogInOwner(string? Email, string? Password, string? UserAgent, IPAddress? ClientAddress)
    : ICommand<LogInOutcome>, IPublic // proof: the password
{
    public override string ToString() => "LogInOwner { the content is hidden }";
}
