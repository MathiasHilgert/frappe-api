namespace Frappe.Identity.Infrastructure.Endpoints;

/// <summary>Only the shape is checked here; whether the code is right is the domain's call.</summary>
public sealed record OwnerVerificationRequest(string? Code);
