using Frappe.Application.Abstractions.Messaging;

namespace Frappe.Identity.Application.Account;

/// <summary>Stores the signed-in owner's language, as the raw tag the client sent.</summary>
public sealed record ChangeLocale(Guid PrincipalId, string? Locale) : ICommand<SharedKernel.Localization.Locale>;
