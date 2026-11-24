using Frappe.Identity.Domain.Codes;
using Frappe.Identity.Infrastructure.Codes;
using Frappe.Identity.Infrastructure.Identifiers;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Identity.Infrastructure.Owners;

/// <summary>
/// What a refused commit means for a unit that sent a verification or a password reset code (UOW-05): a registration (S3), a resend (S6) or a reset
/// request (S11). Two of them raced for
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
            .On(EmailIdentifierConfiguration.NormalizedAddressIndex, DropWhatWasStaged)
            .On(OneTimeCodeConfiguration.LiveIndex, DropWhatWasStaged)
            .OnConcurrency(DropWhatWasStaged);
    }

    private static CommitResolution? DropWhatWasStaged(CommitFailure failure)
    {
        var context = failure.Context;
        var sentVerificationCode = context.ChangeTracker.Entries<OneTimeCode>()
            .Any(entry => entry.State == EntityState.Added && entry.Entity.Purpose is CodePurpose.EmailVerification or CodePurpose.PasswordReset);
        if (!sentVerificationCode)
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
