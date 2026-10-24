using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Domain.Principals;

/// <summary>
/// A plaintext password that meets the length policy (NIST SP 800-63B): 10 to 128 characters and no composition
/// rules. Characters are Unicode code points, so an emoji counts once, not as two UTF-16 units. The value is kept
/// exactly as typed (no trimming) and never appears in <see cref="ToString"/>.
/// </summary>
public sealed class Password
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    private Password(string value) => Value = value;

    public string Value { get; }

    public static Result<Password> Create(string? value)
    {
        var candidate = value ?? string.Empty;
        var length = candidate.EnumerateRunes().Count();

        var notification = new ValidationNotification()
            .AddIf(length < MinLength, IdentityErrors.PasswordTooShort)
            .AddIf(length > MaxLength, IdentityErrors.PasswordTooLong);

        return Result.From(notification, () => new Password(candidate));
    }

    public override string ToString() => "********";
}
