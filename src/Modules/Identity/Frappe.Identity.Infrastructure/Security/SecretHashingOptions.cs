namespace Frappe.Identity.Infrastructure.Security;

/// <summary>The HMAC key for session tokens and verification codes. The host refuses to start without one.</summary>
public sealed class SecretHashingOptions
{
    public const string Section = "Identity:SecretHashing";
    public const int MinimumKeyLength = 32;

    public string? Key { get; set; }

    public bool IsValid() => Key is { Length: >= MinimumKeyLength } && !string.IsNullOrWhiteSpace(Key);
}
