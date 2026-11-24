using Frappe.Aggregates;
using Frappe.Localization;
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

    /// <summary>A pending owner of <paramref name="principalId"/>, registered at <paramref name="now"/>.</summary>
    public static Owner Register(Guid principalId, string givenName, string? familyName, LanguageTag locale, Instant now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(givenName);
        ArgumentNullException.ThrowIfNull(locale);
        return new Owner(principalId, givenName, familyName, locale, now);
    }
}
