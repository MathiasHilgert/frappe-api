namespace Frappe.Identity.Application.Security;

/// <summary>Cryptographically random secrets handed to a person once and stored only as hashes.</summary>
public interface ISecretGenerator
{
    /// <summary>An opaque, URL-safe bearer token.</summary>
    string NewSessionToken();

    /// <summary>A six-digit code a person can type from an email.</summary>
    string NewVerificationCode();
}
