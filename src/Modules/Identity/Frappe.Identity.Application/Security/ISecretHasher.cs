namespace Frappe.Identity.Application.Security;

/// <summary>
/// Keyed, deterministic hashing for high-entropy or short-lived secrets (session tokens, verification codes):
/// the same secret always yields the same hash, so the stored value can be looked up and compared by equality.
/// </summary>
public interface ISecretHasher
{
    string Hash(string secret);
}
