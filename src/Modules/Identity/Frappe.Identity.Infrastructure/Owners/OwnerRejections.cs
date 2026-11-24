using Frappe.Identity.Domain;
using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Identifiers;
using Frappe.Identity.Infrastructure.Codes;
using Frappe.Identity.Infrastructure.Identifiers;
using Frappe.Persistence;
using Frappe.Results;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.Infrastructure.Owners;

/// <summary>
/// What a refused commit means for a unit that sent a verification or a password reset code (UOW-05): a registration (S3), a resend (S6), a reset
/// request (S11) or an email change request (S15, in both branches). Two of them raced for
/// the same address, the same pending owner or the same live code, and the other committed first. The caller is told what a repeat tells
/// ("201" and "202 always"), so the loser drops everything it staged, including the outbox rows that would mail a code that never existed,
/// and its command succeeds. The winner's code and password stand, as if the loser had come first. A unit that sent no such code (a
/// verification, a later flow) is left to the next rules: a unit that spent an undo token, a verification code or an address first loses to
/// the winner and is answered what a repeat is (<see cref="OwnerCommitRejectionException"/>). The unit collected the dropped events before the save,
/// so counters over-count by one in this race.
/// </summary>
internal static class OwnerRejections
{
    public static void Register(CommitRejectionMap rejections)
    {
        ArgumentNullException.ThrowIfNull(rejections);
        rejections
            .On(
                EmailIdentifierConfiguration.NormalizedAddressIndex,
                failure => DropWhatWasStaged(failure) ?? LostTheRace(failure) ?? ChangeLostTheAddress(failure))
            .On(OneTimeCodeConfiguration.LiveIndex, DropWhatWasStaged)
            .OnConcurrency(failure => DropWhatWasStaged(failure) ?? LostTheRace(failure));
    }

    /// <summary>
    /// An email change confirmed an address that another unit took first. Nothing is written and the command is answered that its code is invalid,
    /// as the repeat is, which finds the address held by a verified owner (S15).
    /// </summary>
    private static CommitResolution? ChangeLostTheAddress(CommitFailure failure) =>
        failure.Context.ChangeTracker.Entries<EmailIdentifier>().Any(entry => entry.State == EntityState.Modified)
            ? Reject(IdentityErrorCodes.CodeInvalid, failure)
            : null;

    /// <summary>
    /// A unit that spent a token or a code lost the race for it to a unit that spent it first. Nothing is written and the command is answered what
    /// a repeat is: an undo token that is no longer valid (S15), a verification code that is no longer valid (S3).
    /// </summary>
    private static CommitResolution? LostTheRace(CommitFailure failure)
    {
        var tracker = failure.Context.ChangeTracker;
        if (tracker.Entries<EmailChangeUndo>().Any(entry => entry.State == EntityState.Modified && entry.Entity.RedeemedAt is not null
            && Conflicted(failure, entry.Entity)))
        {
            return Reject(IdentityErrorCodes.UndoInvalid, failure);
        }

        return tracker.Entries<OneTimeCode>().Any(entry => entry.State == EntityState.Modified
            && entry.Entity.Purpose == CodePurpose.EmailVerification && entry.Entity.RedeemedAt is not null)
            ? Reject(IdentityErrorCodes.CodeInvalid, failure)
            : null;
    }

    /// <summary>
    /// Whether the refused write was the one of <paramref name="redeemed"/> itself: another row of an undo (a session touched in the same
    /// minute) can lose its version while the token is still live, and that is a conflict to retry, not a token already spent. A
    /// verification touches no existing session, so any conflict of its unit is a code already spent.
    /// </summary>
    private static bool Conflicted(CommitFailure failure, object redeemed) =>
        failure.Exception is DbUpdateConcurrencyException concurrency && concurrency.Entries.Any(entry => ReferenceEquals(entry.Entity, redeemed));

    private static CommitResolution Reject(string code, CommitFailure failure) =>
        CommitResolution.Reject(new OwnerCommitRejectionException(Error.RuleViolation(code), failure.Exception));

    /// <summary>
    /// The unit was a request to change the address (S15), whichever branch it took: it staged an email change code (a free address), or only
    /// invalidated the live one (a taken address, which stages none). A confirmation is never one of them: it modifies the address.
    /// </summary>
    private static bool AskedForEmailChange(DbContext context, List<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<OneTimeCode>> codes) =>
        !context.ChangeTracker.Entries<EmailIdentifier>().Any(entry => entry.State != EntityState.Unchanged)
        && codes.Any(entry => entry.Entity.Purpose == CodePurpose.EmailChange
            && (entry.State == EntityState.Added || (entry.State == EntityState.Modified && entry.Entity.InvalidatedAt is not null)));

    private static CommitResolution? DropWhatWasStaged(CommitFailure failure)
    {
        var context = failure.Context;
        var codes = context.ChangeTracker.Entries<OneTimeCode>().ToList();
        var sentVerificationCode = codes
            .Any(entry => entry.State == EntityState.Added && entry.Entity.Purpose is CodePurpose.EmailVerification or CodePurpose.PasswordReset);
        if (!sentVerificationCode && !AskedForEmailChange(context, codes))
        {
            return null;
        }

        foreach (var entry in context.ChangeTracker.Entries().Where(entry => entry.State != EntityState.Unchanged).ToList())
        {
            entry.State = EntityState.Detached;
        }

        return CommitResolution.Repair;
    }
}
