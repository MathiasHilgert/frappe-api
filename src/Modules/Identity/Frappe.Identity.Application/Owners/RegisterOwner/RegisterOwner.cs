using Frappe.Access.Contracts;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Owners.RegisterOwner;

/// <summary>
/// Registers an owner (S3): a pending owner with a password and a code mailed to the address. An address a verified owner holds answers the
/// same, writes nothing and tells the real owner; one a pending owner holds is taken over by this registration. It carries a password and an
/// address, so it prints neither (DOM-06).
/// </summary>
/// <param name="GivenName">The given name.</param>
/// <param name="FamilyName">The family name, if any.</param>
/// <param name="Email">The address, as typed.</param>
/// <param name="Password">The password, as typed.</param>
/// <param name="Locale">The language the request was served in (resolved from <c>Accept-Language</c>, never rejected).</param>
public sealed record RegisterOwner(string? GivenName, string? FamilyName, string? Email, string? Password, string? Locale)
    : ICommand<RegisteredOwner>, IPublic // proof: anyone may sign up; the email code proves the address
{
    public override string ToString() => "RegisterOwner { Locale = " + Locale + ", the rest is hidden }";
}
