using Frappe.Application.Abstractions.Messaging;

namespace Frappe.Identity.Application.Sessions;

/// <summary>
/// Makes <paramref name="ClientIp"/> a known sign-in origin of <paramref name="Email"/> (already normalized). Cascaded by
/// a successful sign-in, so it runs only after that sign-in committed; nobody reads an answer.
/// </summary>
public sealed record RememberSignInOrigin(string Email, string ClientIp) : IBackgroundCommand;
