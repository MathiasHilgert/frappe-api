using Frappe.Identity.Domain.Users;
using Frappe.SharedKernel.Localization;

namespace Frappe.Identity.Domain.Principals;

/// <summary>The owner credential of a principal. Changed only through <see cref="Principal"/>.</summary>
public sealed class OwnerAccount
{
    private OwnerAccount()
    {
        Name = null!;
        Email = null!;
        PasswordHash = null!;
        Locale = null!;
    }

    internal OwnerAccount(PersonName name, Email email, string passwordHash, Locale locale)
    {
        Locale = locale;
        Name = name;
        Email = email;
        PasswordHash = passwordHash;
    }

    public PersonName Name { get; }
    public Email Email { get; private set; }
    public string PasswordHash { get; private set; }
    public OwnerStatus Status { get; private set; } = OwnerStatus.Pending;

    /// <summary>The requested new address. The current one keeps working until the change is confirmed.</summary>
    public Email? PendingEmail { get; private set; }

    /// <summary>The language the owner reads: mails and responses without an explicit <c>Accept-Language</c>.</summary>
    public Locale Locale { get; private set; }

    internal void Activate() => Status = OwnerStatus.Active;

    internal void Suspend() => Status = OwnerStatus.Suspended;

    internal void ReplacePasswordHash(string passwordHash) => PasswordHash = passwordHash;

    internal void ChangeLocale(Locale locale) => Locale = locale;

    internal void ParkPendingEmail(Email newEmail) => PendingEmail = newEmail;

    /// <summary>The pending address becomes the current one.</summary>
    internal void ConfirmPendingEmail()
    {
        Email = PendingEmail ?? throw new InvalidOperationException("No pending email to confirm.");
        PendingEmail = null;
    }
}
