using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Domain.Users;

/// <summary>How a person wants to be called. Invariants live here, not in the HTTP layer.</summary>
public sealed record PersonName
{
    public const int MaxLength = 80;

    private PersonName(string value) => Value = value;

    public string Value { get; }

    public static Result<PersonName> Create(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        var notification = new Notification()
            .AddIf(trimmed.Length == 0, IdentityErrors.NameRequired)
            .AddIf(trimmed.Length > MaxLength, IdentityErrors.NameTooLong);

        return Result.From(notification, () => new PersonName(trimmed));
    }

    public override string ToString() => Value;
}
