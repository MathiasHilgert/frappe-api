using System.ComponentModel.DataAnnotations;

namespace Frappe.Identity.Infrastructure.Security;

/// <summary>Where the HaveIBeenPwned range API lives and how long a sign-up waits for it before failing open.</summary>
internal sealed class BreachedPasswordOptions : IValidatableObject
{
    public const string SectionName = "Identity:BreachedPasswords";
    public const int MinimumTimeoutMilliseconds = 100;
    public const int MaximumTimeoutMilliseconds = 10_000;

    public Uri BaseAddress { get; set; } = new("https://api.pwnedpasswords.com/");

    [Range(MinimumTimeoutMilliseconds, MaximumTimeoutMilliseconds)]
    public int TimeoutMilliseconds { get; set; } = 1_500;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BaseAddress is not { IsAbsoluteUri: true })
            yield return new ValidationResult($"'{SectionName}:BaseAddress' must be an absolute URI.", [nameof(BaseAddress)]);
    }
}
