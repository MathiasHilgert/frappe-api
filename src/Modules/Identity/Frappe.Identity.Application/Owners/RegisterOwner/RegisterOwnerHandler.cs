using Frappe.Identity.Application.Codes;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Identifiers;
using Frappe.Identity.Application.Principals;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Credentials;
using Frappe.Identity.Domain.Identifiers;
using Frappe.Identity.Domain.Owners;
using Frappe.Identity.Domain.Principals;
using Frappe.Localization;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;

namespace Frappe.Identity.Application.Owners.RegisterOwner;

/// <summary>
/// Handles <see cref="RegisterOwner"/>. Everything that can be refused is checked, and the password hashed, before the address is looked up,
/// so a taken address costs the caller what a new one does and a refusal never says whether the address exists (SEC-06). A pending owner
/// takes over only when a new code can be sent; a refused send budget leaves it as it was, since the old code would still work with a
/// password the new registrant did not choose. The unique index decides two registrations of one new address (<c>OwnerRejections</c>).
/// </summary>
internal sealed class RegisterOwnerHandler(
    IEmailIdentifiers identifiers,
    IPrincipals principals,
    IOwners owners,
    IPasswordCredentials credentials,
    PasswordPolicy policy,
    IPasswordHasher hasher,
    CodeIssuer codes,
    TimeProvider time) : ICommandHandler<RegisterOwner, RegisteredOwner>
{
    public async Task<Result<RegisteredOwner>> HandleAsync(RegisterOwner command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var email = Email.Create(command.Email);
        var name = OwnerName.Create(command.GivenName, command.FamilyName);
        var password = await policy.AcceptAsync(command.Password, cancellationToken);
        var valid = Result.Combine(email, name, password);
        if (!valid.IsSuccess)
        {
            return Result.Failure<RegisteredOwner>(valid.Errors);
        }

        var hash = hasher.Hash(password.Value);
        var locale = ServedLocale(command.Locale);
        var now = Instant.FromDateTimeOffset(time.GetUtcNow());
        var existing = await identifiers.FindByAddressAsync(email.Value.NormalizedAddress, cancellationToken);
        return existing is null
            ? await RegisterNewAsync(email.Value, name.Value, hash, locale, now, cancellationToken)
            : await AnswerForTakenAsync(existing, name.Value, hash, locale, now, cancellationToken);
    }

    private async Task<Result<RegisteredOwner>> RegisterNewAsync(
        Email email, OwnerName name, PasswordHash hash, LanguageTag locale, Instant now, CancellationToken cancellationToken)
    {
        var principal = Principal.Create(PrincipalKind.Owner, PrincipalStatus.Pending, now);
        principals.Add(principal);
        identifiers.Add(EmailIdentifier.Create(principal.Id, email.Address, email.NormalizedAddress, verifiedAt: null));
        credentials.Add(PasswordCredential.Set(principal.Id, hash, now));
        var owner = Owner.Register(principal.Id, name, locale, now);
        owners.Add(owner);
        await IssueCodeAsync(owner, email.Address, email.NormalizedAddress, now, cancellationToken);
        return new RegisteredOwner(principal.Id);
    }

    private async Task<Result<RegisteredOwner>> AnswerForTakenAsync(
        EmailIdentifier existing, OwnerName name, PasswordHash hash, LanguageTag locale, Instant now, CancellationToken cancellationToken)
    {
        var owner = await owners.FindAsync(existing.PrincipalId, cancellationToken);
        if (owner is null)
        {
            // The address belongs to a principal of another kind: nothing to replace and nobody here to tell.
            return new RegisteredOwner(Guid.CreateVersion7());
        }

        if (owner.VerifiedAt is not null)
        {
            owner.RecordRegistrationAttempted(existing.Address, now);
            return new RegisteredOwner(Guid.CreateVersion7());
        }

        // The send budget is asked first: a refusal stages nothing, so the pending owner and its live code stay untouched.
        var issued = await codes.IssueAsync(owner.PrincipalId, CodePurpose.EmailVerification, existing.NormalizedAddress, cancellationToken);
        if (!issued.IsSuccess)
        {
            return new RegisteredOwner(owner.PrincipalId);
        }

        var replaced = owner.Replace(name, locale, now);
        if (!replaced.IsSuccess)
        {
            return Result.Failure<RegisteredOwner>(replaced.Errors);
        }

        var credential = await credentials.FindAsync(owner.PrincipalId, cancellationToken);
        if (credential is null)
        {
            credentials.Add(PasswordCredential.Set(owner.PrincipalId, hash, now));
        }
        else
        {
            credential.Replace(hash, now);
        }

        owner.RecordCodeIssued(CodePurpose.EmailVerification, existing.Address, issued.Value.Plaintext, now);
        return new RegisteredOwner(owner.PrincipalId);
    }

    private async Task IssueCodeAsync(Owner owner, string address, string normalizedAddress, Instant now, CancellationToken cancellationToken)
    {
        var issued = await codes.IssueAsync(owner.PrincipalId, CodePurpose.EmailVerification, normalizedAddress, cancellationToken);
        if (issued.IsSuccess)
        {
            owner.RecordCodeIssued(CodePurpose.EmailVerification, address, issued.Value.Plaintext, now);
        }
    }

    /// <summary>The language of the request when Frappé is written in it, English otherwise: the locale is resolved, never rejected.</summary>
    private static LanguageTag ServedLocale(string? raw) =>
        LanguageTag.Create(raw) is { IsSuccess: true } tag && LanguageFallback.IsServed(tag.Value) ? tag.Value : LanguageFallback.English;
}
