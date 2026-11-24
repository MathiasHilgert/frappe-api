using Frappe.Identity.Application.Codes;
using Frappe.Identity.Application.Credentials;
using Frappe.Identity.Application.Identifiers;
using Frappe.Identity.Application.Limits;
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
/// so a taken address costs the caller what a new one does and a refusal never says whether the address exists (SEC-06). Every answer
/// carries a fresh id, whatever holds the address, so the id tells nothing; a pending owner is replaced by a new principal, so the id and the
/// code it was given stop working. The notice mail to a verified owner is bounded per address (<see cref="RegistrationLimits.Notice"/>).
/// The unique index decides two registrations of one new address (<c>OwnerRejections</c>).
/// </summary>
internal sealed class RegisterOwnerHandler(
    IEmailIdentifiers identifiers,
    IPrincipals principals,
    IOwners owners,
    IPasswordCredentials credentials,
    PasswordPolicy policy,
    IPasswordHasher hasher,
    CodeIssuer codes,
    IAttemptLimiter limiter,
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
        if (existing is null)
        {
            return await RegisterAsync(email.Value, name.Value, hash, locale, now, replaced: null, cancellationToken);
        }

        var owner = await owners.FindAsync(existing.PrincipalId, cancellationToken);
        if (owner is null)
        {
            // The address belongs to a principal of another kind: nothing to replace and nobody here to tell.
            return new RegisteredOwner(Guid.CreateVersion7());
        }

        if (owner.VerifiedAt is not null)
        {
            // Only a refusal of the limiter holds the notice back: the flow fails open, and the answer is the same either way.
            var verdict = await limiter.HitAsync(
                RegistrationLimits.Notice, RegistrationLimits.Subject(email.Value.NormalizedAddress), cancellationToken);
            if (verdict.Outcome != AttemptOutcome.Refused)
            {
                owner.RecordRegistrationAttempted(existing.Address, now);
            }

            return new RegisteredOwner(Guid.CreateVersion7());
        }

        return await RegisterAsync(email.Value, name.Value, hash, locale, now, owner, cancellationToken);
    }

    /// <summary>
    /// Registers a new principal for the address. When <paramref name="replaced"/> holds the address, its principal goes in the same unit
    /// with its address, password, owner and codes, so the id and the code it was given stop working and the one answered here is as new as
    /// any other. The send budget counts per principal, so the new one starts with a fresh budget.
    /// </summary>
    private async Task<Result<RegisteredOwner>> RegisterAsync(
        Email email, OwnerName name, PasswordHash hash, LanguageTag locale, Instant now, Owner? replaced, CancellationToken cancellationToken)
    {
        var principal = Principal.Create(PrincipalKind.Owner, PrincipalStatus.Pending, now);
        Owner owner;
        if (replaced is null)
        {
            owner = Owner.Register(principal.Id, name, locale, now);
        }
        else
        {
            var replacement = replaced.ReplaceWith(principal.Id, name, locale, now);
            if (!replacement.IsSuccess)
            {
                return Result.Failure<RegisteredOwner>(replacement.Errors);
            }

            owner = replacement.Value;
            principals.Remove((await principals.FindAsync(replaced.PrincipalId, cancellationToken))!);
        }

        principals.Add(principal);
        identifiers.Add(EmailIdentifier.Create(principal.Id, email.Address, email.NormalizedAddress, verifiedAt: null));
        credentials.Add(PasswordCredential.Set(principal.Id, hash, now));
        owners.Add(owner);
        await IssueCodeAsync(owner, email.Address, email.NormalizedAddress, now, cancellationToken);
        return new RegisteredOwner(principal.Id);
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
