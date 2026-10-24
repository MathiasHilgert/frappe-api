using Frappe.Application.Abstractions.Messaging;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;
using Wolverine;

namespace Frappe.Identity.Application.Owners;

/// <summary>Sends a pending owner a new verification code, replacing the previous one. At most five per hour.</summary>
public sealed record ResendVerificationCode(Guid PrincipalId) : ICommand;
