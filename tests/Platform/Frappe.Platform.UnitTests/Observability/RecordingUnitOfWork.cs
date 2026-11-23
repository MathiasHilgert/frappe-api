using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>
/// A unit of work that only remembers how the pipeline drove it; <see cref="FailCommit"/> makes the commit throw and
/// <see cref="BeginFailure"/> makes the begin throw that exception.
/// </summary>
public sealed class RecordingUnitOfWork : IUnitOfWork
{
    public List<string> Calls { get; } = [];

    public bool FailCommit { get; init; }

    public Exception? BeginFailure { get; init; }

    public Task BeginAsync(CancellationToken cancellationToken)
    {
        Calls.Add("begin");
        return BeginFailure is null ? Task.CompletedTask : throw BeginFailure;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Calls.Add("save");
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Calls.Add("commit");
        return FailCommit ? throw new InvalidOperationException("The database refused the commit.") : Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken)
    {
        Calls.Add("rollback");
        return Task.CompletedTask;
    }
}
