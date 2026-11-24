using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Domain.Identifiers;
using Frappe.Identity.Infrastructure.Codes;
using Frappe.Identity.Infrastructure.Identifiers;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.Infrastructure.Owners;

/// <summary>
/// What a refused commit means for a unit that sent a verification or a password reset code (UOW-05): a registration (S3), a resend (S6), a reset
/// request (S11) or an email change request (S15, in both branches). Two of them raced for
/// the same address, the same pending owner or the same live code, and the other committed first. The caller is told what a repeat tells
/// ("201" and "202 always"), so the loser drops everything it staged, including the outbox rows that would mail a code that never existed,
/// and its command succeeds. The winner's code and password stand, as if the loser had come first. A unit that sent no such code (a
/// verification, a later flow) is left to the next rule. The unit collected the dropped events before the save, so counters over-count by one
/// in this race.
/// </summary>
internal static class OwnerRejections
{
    public static void Register(CommitRejectionMap rejections)
    {
        ArgumentNullException.ThrowIfNull(rejections);
        rejections
            .On(EmailIdentifierConfiguration.NormalizedAddressIndex, failure => DropWhatWasStaged(failure) ?? ChangeLostTheAddress(failure))
            .On(OneTimeCodeConfiguration.LiveIndex, DropWhatWasStaged)
            .OnConcurrency(DropWhatWasStaged);
    }

    /// <summary>
    /// An email change confirmed an address that another unit took first. Nothing is written and the command is answered a commit conflict; the
    /// repeat finds the address held and answers as any refused change does.
    /// </summary>
    private static CommitResolution? ChangeLostTheAddress(CommitFailure failure) =>
        failure.Context.ChangeTracker.Entries<EmailIdentifier>().Any(entry => entry.State == EntityState.Modified)
            ? CommitResolution.Reject(new CommitConflictException("Another request took the address first.", failure.Exception))
            : null;

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
