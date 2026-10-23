namespace Frappe.SharedKernel.Validation;

/// <summary>
/// A broken rule. <see cref="Code"/> is stable and machine-readable and doubles as the message catalogue key;
/// <see cref="Message"/> is the English fallback; <see cref="Field"/> points at the offending input when there is one.
/// </summary>
public sealed record Error(string Code, string Message, string? Field = null, ErrorKind Kind = ErrorKind.Validation);
