using System.ComponentModel.DataAnnotations;

namespace Frappe.Identity.Infrastructure.Credentials;

/// <summary>
/// The breach list a new password is checked against (S4), bound from <c>Identity:BreachCheck</c>. The list is HaveIBeenPwned's range API:
/// only the first five characters of the password's SHA-1 leave the process. When it cannot answer in time the password is accepted.
/// </summary>
public sealed class BreachCheckOptions : IValidatableObject
{
    /// <summary>The section the options are bound from.</summary>
    public const string Section = "Identity:BreachCheck";

    /// <summary>
    /// <c>Identity:BreachCheck:BaseUrl</c>: the range API's address. Default <c>https://api.pwnedpasswords.com/</c>; an absolute http or
    /// https address. Pointing it elsewhere sends the hash prefixes there: use it only for a mirror you run.
    /// </summary>
    [Required(ErrorMessage = "Configuration 'Identity:BreachCheck:BaseUrl' is required.")]
    public Uri BaseUrl { get; set; } = new("https://api.pwnedpasswords.com/");

    /// <summary>
    /// <c>Identity:BreachCheck:Timeout</c>: the longest one check waits for the list; after it the password is accepted. Default 1 second,
    /// range 0.05 to 10 seconds. Raising it slows registration and password changes while the list is slow; lowering it accepts more
    /// passwords unchecked.
    /// </summary>
    [OptionRange("Identity:BreachCheck:Timeout", 0.05, 10)]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// <c>Identity:BreachCheck:BreakDuration</c>: how long the breaker leaves the list alone after it keeps failing, accepting passwords
    /// unchecked meanwhile. Default 30 seconds, range 1 to 600 seconds. Raising it spares a failing list longer and checks less.
    /// </summary>
    [OptionRange("Identity:BreachCheck:BreakDuration", 1, 600)]
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BaseUrl is not null && (!BaseUrl.IsAbsoluteUri || (BaseUrl.Scheme != Uri.UriSchemeHttps && BaseUrl.Scheme != Uri.UriSchemeHttp)))
        {
            yield return new ValidationResult(
                "Configuration 'Identity:BreachCheck:BaseUrl' must be an absolute http or https address.", [nameof(BaseUrl)]);
        }
    }
}
