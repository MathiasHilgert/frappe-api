using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Domain.Principals;

/// <summary>
/// Where an owner signs in and receives codes. Kept as typed; two spellings that differ only in case
/// are the same address, which is also what the unique index on lower(email) enforces.
/// </summary>
public sealed record Email
{
    public const int MaxLength = 254;

    private Email(string value) => Value = value;

    public string Value { get; }

    public static Result<Email> Create(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        var notification = new ValidationNotification()
            .AddIf(trimmed.Length == 0, IdentityErrors.EmailRequired)
            .AddIf(trimmed.Length > 0 && !IsWellFormed(trimmed), IdentityErrors.EmailInvalid);

        return Result.From(notification, () => new Email(trimmed));
    }

    public bool Equals(Email? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString() => Value;

    // Deliverability is proven by the verification code, so the shape check stays deliberately loose.
    private static bool IsWellFormed(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return value.Length <= MaxLength && at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1;
    }
}
