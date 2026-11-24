using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Owners.ResendOwnerVerification;

/// <summary>
/// Sends a pending owner a new verification code (S6). It always succeeds, whether the id is malformed, unknown, verified, or over its send
/// budget, so the answer reveals nothing about which owners exist.
/// </summary>
/// <param name="OwnerId">The id the caller was given when registering, as text.</param>
public sealed record ResendOwnerVerification(string? OwnerId) : ICommand<Unit>;
