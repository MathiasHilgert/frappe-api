using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Principals;
using Frappe.SharedKernel.Validation;
using Wolverine;

namespace Frappe.Identity.Application.Owners;

/// <summary>The outcome; <see cref="RetryAfter"/> is set only when the hourly cap was reached.</summary>
public sealed record ResendVerificationCodeResult(Result Result, TimeSpan RetryAfter);
