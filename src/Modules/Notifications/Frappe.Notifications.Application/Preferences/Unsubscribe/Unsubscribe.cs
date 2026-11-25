using Frappe.Access.Contracts;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Notifications.Application.Preferences.Unsubscribe;

/// <summary>
/// Turns a category off for the recipient a signed token names (S5): the one-click unsubscribe of a Marketing mail. It only turns a category
/// off, never on, and only a category that can be turned off; doing it again changes nothing and still succeeds. A token that is not ours is
/// refused as <c>notifications.unsubscribe_token.invalid</c> before anything is read.
/// </summary>
/// <param name="Token">The opaque token of the unsubscribe link; it is the caller's only credential and is never printed.</param>
public sealed record Unsubscribe(string? Token) : ICommand<Unit>, IPublic // proof: the signed unsubscribe token
{
    public override string ToString() => "Unsubscribe { Token = *** }";
}
