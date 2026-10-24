namespace Frappe.Identity.Application.Principals;

/// <summary>A pending owner past the purge cutoff, and the keyset position the next page starts after.</summary>
public sealed record PurgeCandidate(Guid Id, DateTimeOffset RegisteredAt);
