using Frappe.Access.Contracts;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Sessions;

namespace Frappe.Identity.Application.Sessions;

/// <summary>
/// The rules of <see cref="Session"/>: held by its principal, seen only by its holder (anyone else is answered the session's own not-found, so
/// nothing is learned about other principals' sessions, SEC-05) and revoked only by its holder.
/// </summary>
public sealed class SessionRules : IRules
{
    /// <inheritdoc />
    public void Define(RuleBook book)
    {
        ArgumentNullException.ThrowIfNull(book);
        book.For<Session>(notFound: IdentityErrorCodes.SessionNotFound)
            .HeldBy(session => session.PrincipalId)
            .Visible(Is.Holder)
            .Allow(SessionActions.Revoke, Is.Holder);
    }
}
