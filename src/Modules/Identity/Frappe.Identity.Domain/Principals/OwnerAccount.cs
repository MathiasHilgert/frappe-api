using Frappe.Identity.Domain.Users;

namespace Frappe.Identity.Domain.Principals;

/// <summary>The owner credential of a principal. Changed only through <see cref="Principal"/>.</summary>
public sealed class OwnerAccount
{
    private OwnerAccount()
    {
        Name = null!;
        Email = null!;
        PasswordHash = null!;
    }

    internal OwnerAccount(PersonName name, Email email, string passwordHash)
    {
        Name = name;
        Email = email;
        PasswordHash = passwordHash;
    }

    public PersonName Name { get; }
    public Email Email { get; internal set; }
    public string PasswordHash { get; internal set; }
    public OwnerStatus Status { get; internal set; } = OwnerStatus.Pending;

    /// <summary>The requested new address. The current one keeps working until the change is confirmed.</summary>
    public Email? PendingEmail { get; internal set; }
}
