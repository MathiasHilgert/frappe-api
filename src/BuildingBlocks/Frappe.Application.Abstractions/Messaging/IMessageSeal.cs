namespace Frappe.Application.Abstractions.Messaging;

/// <summary>
/// Seals a secret a message must carry (a one-time code for the mail that delivers it), so the outbox and every queue
/// that persists the message only ever hold ciphertext. The publisher seals; the one reader opens it in memory.
/// A platform abstraction, so a module's Contracts stay messages only and no module hosts a service for another.
/// </summary>
public interface IMessageSeal
{
    string Seal(string secret);

    /// <summary>The secret inside <paramref name="sealedSecret"/>; throws when it was tampered with or sealed by another key.</summary>
    string Open(string sealedSecret);
}
