using System.ComponentModel.DataAnnotations;

namespace Frappe.Identity.Infrastructure.Security;

/// <summary>The HMAC key for session tokens and verification codes. The host refuses to start without one.</summary>
public sealed class SecretHashingOptions
{
    public const string Section = "Identity:SecretHashing";
    public const int MinimumKeyLength = 32;

    // Required rejects null, empty and whitespace-only keys.
    [Required(ErrorMessage = $"'{Section}:Key' is missing.")]
    [MinLength(MinimumKeyLength, ErrorMessage = $"'{Section}:Key' must be at least {{1}} characters.")]
    public string? Key { get; set; }
}
