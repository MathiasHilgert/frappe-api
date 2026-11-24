using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Owners;
using Frappe.Localization;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Identity.Application.Owners.UpdateMe;

/// <summary>
/// Handles <see cref="UpdateMe"/>. The new name and language are checked together, so a patch with two faults reports both and changes nothing.
/// A precondition that no longer holds is reported only when the patch itself is valid (HTTP-20); the row's own version also guards the commit,
/// so a change that slips in between is refused as a conflict.
/// </summary>
internal sealed class UpdateMeHandler(IOwners owners) : ICommandHandler<UpdateMe, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UpdateMe command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var owner = await owners.FindAsync(command.PrincipalId, cancellationToken);
        if (owner is null)
        {
            return new UnauthenticatedError(IdentityErrorCodes.SessionInvalid);
        }

        var name = NameOf(owner, command);
        var locale = command.Locale is null ? Result.Success<LanguageTag?>(null) : LocaleOf(command.Locale.Value);
        var checkedPatch = Result.Combine(name, locale);
        if (!checkedPatch.IsSuccess)
        {
            return Result.Failure<Unit>(checkedPatch.Errors);
        }

        if (!Holds(command.IfMatch, owners.VersionOf(owner)))
        {
            return new PreconditionFailedError(IdentityErrorCodes.ProfileChanged);
        }

        if (name.Value is { } newName)
        {
            owner.Rename(newName);
        }

        if (locale.Value is { } newLocale)
        {
            owner.ChangeLocale(newLocale);
        }

        return Unit.Value;
    }

    private static Result<OwnerName?> NameOf(Owner owner, UpdateMe command)
    {
        if (command.Given is null && command.Family is null)
        {
            return Result.Success<OwnerName?>(null);
        }

        return OwnerName.Create(
            command.Given is null ? owner.GivenName : command.Given.Value,
            command.Family is null ? owner.FamilyName : command.Family.Value).Map(valid => (OwnerName?)valid);
    }

    private static bool Holds(IReadOnlyList<string>? accepted, string version) =>
        accepted is null || accepted.Contains(UpdateMe.AnyVersion) || accepted.Contains(version);

    private static Result<LanguageTag?> LocaleOf(string? raw)
    {
        var tag = LanguageTag.Create(raw);
        if (!tag.IsSuccess)
        {
            return Result.Failure<LanguageTag?>(tag.Errors);
        }

        return LanguageFallback.IsServed(tag.Value)
            ? Result.Success<LanguageTag?>(tag.Value)
            : Error.RuleViolation(IdentityErrorCodes.LocaleUnsupported);
    }
}
