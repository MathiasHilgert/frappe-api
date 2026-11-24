using Frappe.Aggregates;
using Frappe.Identity.Domain.Codes;
using Frappe.Localization;
using Frappe.Results;
using NodaTime;

// EF Core materializes the aggregate through its private constructor and setters.
#pragma warning disable S1144, S3453
namespace Frappe.Identity.Domain.Owners;

/// <summary>
/// What an owner is beyond a principal: a name and a language. It is keyed by its principal's id and lives in the Owners feature, so the core
/// (principals, identifiers, credentials, sessions, codes) never refers to it. The principal's status says whether the owner is verified.
/// </summary>
public sealed class Owner : AggregateRoot
{
    private Owner()
    {
        GivenName = null!;
        Locale = null!;
    }

    private Owner(Guid principalId, string givenName, string? familyName, LanguageTag locale, Instant now)
    {
        PrincipalId = principalId;
        GivenName = givenName;
        FamilyName = familyName;
        Locale = locale;
        RegisteredAt = now;
        Raise(new OwnerRegistered(principalId, Replaced: false, now));
    }

    /// <summary>The principal it belongs to; also its key.</summary>
    public Guid PrincipalId { get; private set; }

    public string GivenName { get; private set; }

    public string? FamilyName { get; private set; }

    /// <summary>The language of the mail and messages the owner receives.</summary>
    public LanguageTag Locale { get; private set; }

    /// <summary>When the registration was made or last replaced: the pending-owner purge counts from it.</summary>
    public Instant RegisteredAt { get; private set; }

    /// <summary>When the owner proved their address; empty while pending.</summary>
    public Instant? VerifiedAt { get; private set; }

    /// <summary>The name as a mail says it: given plus family.</summary>
    public string MailedName => string.IsNullOrWhiteSpace(FamilyName) ? GivenName : $"{GivenName} {FamilyName}";

    /// <summary>A pending owner of <paramref name="principalId"/> called <paramref name="name"/>, registered at <paramref name="now"/>.</summary>
    public static Owner Register(Guid principalId, OwnerName name, LanguageTag locale, Instant now)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Register(principalId, name.Given, name.Family, locale, now);
    }

    /// <summary>A pending owner of <paramref name="principalId"/>, registered at <paramref name="now"/>.</summary>
    public static Owner Register(Guid principalId, string givenName, string? familyName, LanguageTag locale, Instant now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(givenName);
        ArgumentNullException.ThrowIfNull(locale);
        return new Owner(principalId, givenName, familyName, locale, now);
    }

    /// <summary>
    /// A newer registration of the same address takes this pending owner over: new name and language, and the purge clock restarts. The
    /// caller replaces the password and the code. A verified owner is never replaced.
    /// </summary>
    public Result<Unit> Replace(OwnerName name, LanguageTag locale, Instant now)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(locale);
        if (VerifiedAt is not null)
        {
            return Error.RuleViolation(IdentityErrorCodes.OwnerNotPending);
        }

        GivenName = name.Given;
        FamilyName = name.Family;
        Locale = locale;
        RegisteredAt = now;
        Raise(new OwnerRegistered(PrincipalId, Replaced: true, now));
        return Unit.Value;
    }

    /// <summary>
    /// Records that the owner proved their address at <paramref name="now"/>. It never goes before the registration (a database check), and
    /// an owner that was verified already answers <see cref="IdentityErrorCodes.CodeInvalid"/>, so a code never verifies twice.
    /// </summary>
    public Result<Unit> Verify(Instant now)
    {
        if (VerifiedAt is not null)
        {
            return Error.RuleViolation(IdentityErrorCodes.CodeInvalid);
        }

        VerifiedAt = Instant.Max(now, RegisteredAt);
        Raise(new OwnerVerified(PrincipalId, VerifiedAt.Value));
        return Unit.Value;
    }

    /// <summary>
    /// Records that a code of <paramref name="purpose"/> was issued to this owner for <paramref name="email"/> (the new address for an email
    /// change): the name, the language and the plain text travel in the fact so the mail needs nothing else (S17).
    /// </summary>
    public void RecordCodeIssued(CodePurpose purpose, string email, string plaintext, Instant now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        Raise(new OwnerCodeIssued(PrincipalId, purpose, email, MailedName, Locale.Value, plaintext, now));
    }

    /// <summary>Records that somebody tried to register this verified owner's address; the owner gets a notice in their language (S3).</summary>
    public void RecordRegistrationAttempted(string email, Instant now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        Raise(new OwnerRegistrationAttempted(email, Locale.Value, now));
    }

    /// <summary>
    /// Records that the owner proved their password at <paramref name="now"/>; <paramref name="verificationPending"/> says no session opened
    /// because the address is not proven yet.
    /// </summary>
    public void RecordLogIn(bool verificationPending, Instant now) => Raise(new OwnerLoggedIn(PrincipalId, verificationPending, now));

    /// <summary>Changes the name the owner is called by.</summary>
    public void Rename(OwnerName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        GivenName = name.Given;
        FamilyName = name.Family;
    }

    /// <summary>Changes the language of the mail and messages the owner receives.</summary>
    public void ChangeLocale(LanguageTag locale)
    {
        ArgumentNullException.ThrowIfNull(locale);
        Locale = locale;
    }
}
