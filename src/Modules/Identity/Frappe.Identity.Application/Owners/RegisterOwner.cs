using Frappe.Identity.Application.Principals;
using Frappe.Identity.Application.Security;
using Frappe.Identity.Contracts;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.Identity.Domain.Users;
using Frappe.SharedKernel.Localization;
using Frappe.SharedKernel.Validation;
using Wolverine;

namespace Frappe.Identity.Application.Owners;

/// <summary>
/// Signs up a new owner: the account starts pending and an email-verification code is issued. <paramref name="Locale"/>
/// is the one negotiated for the sign-up request, already one the platform speaks.
/// </summary>
public sealed record RegisterOwner(string? Name, string? Email, string? Password, Locale Locale);
