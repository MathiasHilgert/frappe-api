namespace Frappe.Identity.Application.Security;

/// <summary>Slow, salted password hashing. Two hashes of one password differ, so compare with <see cref="Verify"/>.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>False for a wrong password and for a hash this hasher cannot read; never throws on either.</summary>
    bool Verify(string password, string passwordHash);
}
