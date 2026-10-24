namespace Frappe.Identity.Application.Security;

/// <summary>
/// Slow, salted password hashing. Two hashes of one password differ, so compare with <see cref="VerifyAsync"/>.
/// Both operations are CPU and memory heavy, so implementations bound how many run at once and callers await a slot.
/// </summary>
public interface IPasswordHasher
{
    Task<string> HashAsync(string password, CancellationToken cancellationToken);

    /// <summary>False for a wrong password and for a hash this hasher cannot read; never throws on either.</summary>
    Task<bool> VerifyAsync(string password, string passwordHash, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="passwordHash"/> was made with a weaker cost than the current one.</summary>
    bool NeedsRehash(string passwordHash);
}
