using Frappe.Application.Abstractions.Messaging;
using Frappe.SharedKernel.Localization;

namespace Frappe.Identity.Application.Account;

/// <summary>Stores the signed-in owner's language. The caller already checked the platform speaks it.</summary>
public sealed record ChangeLocale(Guid PrincipalId, Locale Locale) : ICommand;
