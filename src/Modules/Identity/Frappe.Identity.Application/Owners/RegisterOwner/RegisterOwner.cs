using Frappe.Application.Abstractions.Messaging;
using Frappe.SharedKernel.Localization;

namespace Frappe.Identity.Application.Owners;

/// <summary>
/// Signs up a new owner: the account starts pending and an email-verification code is issued. <paramref name="Locale"/>
/// is the one negotiated for the sign-up request, already one the platform speaks; <paramref name="ClientIp"/> is metered.
/// </summary>
public sealed record RegisterOwner(string? Name, string? Email, string? Password, Locale Locale, string ClientIp) : ICommand<Guid>;
