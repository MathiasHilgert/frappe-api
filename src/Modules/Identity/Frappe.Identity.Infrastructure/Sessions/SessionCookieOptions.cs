using System.ComponentModel.DataAnnotations;

namespace Frappe.Identity.Infrastructure.Sessions;

/// <summary>Where the web console keeps its session token. Always HttpOnly, Secure and SameSite=Lax.</summary>
internal sealed class SessionCookieOptions : IValidatableObject
{
    public const string SectionName = "Identity:SessionCookie";

    /// <summary>
    /// Browsers only accept a <c>__Host-</c> cookie that is Secure, has Path=/ and no Domain, so a sibling subdomain
    /// or a plain-HTTP page cannot plant a cookie of the same name to shadow the real one.
    /// </summary>
    public const string HostPrefix = "__Host-";

    // A cookie name is an RFC 6265 token: no separators, spaces or control characters.
    [Required(ErrorMessage = $"'{SectionName}:Name' is missing.")]
    [RegularExpression(@"^[!#$%&'*+\-.^_`|~0-9A-Za-z]+$", ErrorMessage = $"'{SectionName}:Name' must be a cookie-safe token.")]
    public string Name { get; set; } = HostPrefix + "frappe_session";

    [Required(ErrorMessage = $"'{SectionName}:Path' is missing.")]
    [RegularExpression("^/.*$", ErrorMessage = $"'{SectionName}:Path' must start with '/'.")]
    public string Path { get; set; } = "/";

    /// <summary>Only for names without the <see cref="HostPrefix"/>; left unset, the cookie is host-only.</summary>
    public string? Domain { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name?.StartsWith(HostPrefix, StringComparison.Ordinal) == true && (Path != "/" || !string.IsNullOrEmpty(Domain)))
        {
            yield return new ValidationResult(
                $"'{SectionName}': a {HostPrefix} Name needs Path '/' and no Domain.", [nameof(Path), nameof(Domain)]);
        }
    }
}
