namespace Frappe.Identity.Contracts;

/// <summary>
/// Seals the one-time codes carried by identity's code messages, so the outbox and every queue that persists them
/// only ever hold ciphertext. Identity seals when it issues a code; the mailer opens it just before sending.
/// </summary>
public interface IOwnerCodeSeal
{
    string Seal(string code);

    /// <summary>The code inside <paramref name="sealedCode"/>; throws when it was tampered with or sealed by another key.</summary>
    string Open(string sealedCode);
}
