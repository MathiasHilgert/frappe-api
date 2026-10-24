using Frappe.Application.Abstractions.Messaging;
namespace Frappe.Identity.Application.Principals.Purge;

/// <summary>Deletes unverified registrations past their grace. Published daily by a recurring job; nobody reads an answer.</summary>
public sealed record PurgePendingOwners : IBackgroundCommand;
