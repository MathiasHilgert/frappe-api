using Frappe.Application.Abstractions.Messaging;

namespace Frappe.Identity.Application.Owners;

/// <summary>
/// Activates a pending owner with the code emailed at registration and signs them in at once (S7): the session comes
/// back for Bearer clients, and the endpoint hands it to the web console as a cookie.
/// </summary>
public sealed record VerifyOwnerEmail(Guid OwnerId, string Code) : ICommand;
