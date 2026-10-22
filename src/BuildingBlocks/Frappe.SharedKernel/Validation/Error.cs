namespace Frappe.SharedKernel.Validation;

/// <summary>A broken rule. <see cref="Code"/> is stable and machine-readable; <see cref="Field"/> points at the offending input when there is one.</summary>
public sealed record Error(string Code, string Message, string? Field = null);
