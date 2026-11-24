namespace Frappe.Access.Contracts;

/// <summary>
/// Marks a request anyone may run because it proves itself some other way (a password, a signed token, a webhook signature) or is a trusted
/// entry. Choosing it is a visible decision, not an omission: an unmarked request is a startup error.
/// </summary>
public interface IPublic;
