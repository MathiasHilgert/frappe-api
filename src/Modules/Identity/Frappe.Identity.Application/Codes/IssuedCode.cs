using NodaTime;

namespace Frappe.Identity.Application.Codes;

/// <summary>
/// A code just issued: its id and expiry, and the plaintext, which exists only here and in the event that mails it. It prints nothing of the
/// text (DOM-06, SEC-07).
/// </summary>
/// <param name="Id">The id of the stored code.</param>
/// <param name="Plaintext">The six digits to send.</param>
/// <param name="ExpiresAt">When the code stops working.</param>
public sealed record IssuedCode(Guid Id, string Plaintext, Instant ExpiresAt)
{
    public override string ToString() => $"IssuedCode {{ Id = {Id}, Plaintext = ***, ExpiresAt = {ExpiresAt} }}";
}
