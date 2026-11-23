using Frappe.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Frappe.Persistence;

/// <summary>
/// Gives a save made outside the unit of work (the outbox relay's and the inbox consumer's) the module's rejection too, so a lost race
/// there fails with the same exception the commit of a command would (UOW-05). Inside the unit of work the unit decides
/// itself, since its rules may also repair the staged changes and save again; a repair needs the caller's loop, so
/// outside it a rule that asks to retry leaves the failure unchanged.
/// </summary>
internal sealed class CommitRejectionInterceptor(CommitRejectionMap rejections) : SaveChangesInterceptor
{
    /// <summary>Whether the current flow is a unit of work saving, which translates failures itself.</summary>
    private static readonly AsyncLocal<bool> InUnitOfWork = new();

    /// <summary>Marks the saves of the current flow as the unit's until the returned scope is disposed.</summary>
    public static IDisposable EnterUnitOfWork()
    {
        var previous = InUnitOfWork.Value;
        InUnitOfWork.Value = true;
        return new Leave(previous);
    }

    public override async Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        await RejectAsync(eventData.Context, eventData.Exception, cancellationToken);
        await base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    /// <summary>EF reports a lost concurrent change here, not as a failed save.</summary>
    public override async ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
        ConcurrencyExceptionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        await RejectAsync(eventData.Context, eventData.Exception, cancellationToken);
        return await base.ThrowingConcurrencyExceptionAsync(eventData, result, cancellationToken);
    }

    private async Task RejectAsync(DbContext? context, Exception exception, CancellationToken cancellationToken)
    {
        // The rejection thrown for a lost concurrent change comes back wrapped by EF's own save failure: unwrap it.
        if (exception.InnerException is ICommitRejection and Exception wrapped)
        {
            throw wrapped;
        }

        if (!InUnitOfWork.Value && context is not null
            && await rejections.ResolveAsync(new CommitFailure(context, exception, cancellationToken)) is { Rejection: { } rejection })
        {
            throw rejection;
        }
    }

    /// <summary>Restores the previous in-unit-of-work flag when the scope ends.</summary>
    private sealed class Leave(bool previous) : IDisposable
    {
        public void Dispose() => InUnitOfWork.Value = previous;
    }
}
