using Frappe.UseCases;
using Microsoft.EntityFrameworkCore;

namespace Frappe.Persistence;

/// <summary>
/// The rules of one module, in the order the module registered them (UOW-05): the first rule that matches the failure
/// and decides (returns a <see cref="CommitResolution"/>) wins; a rule that returns <see langword="null"/> leaves the
/// failure to the next one, and a failure no rule decides passes through unchanged. The rules of
/// <see cref="OnDeferred{TRejection}"/> are kept apart: they decide only the failure of a deferrable constraint the unit
/// checks right before it commits (UOW-05).
/// </summary>
public sealed class CommitRejectionMap
{
    private readonly List<(string Constraint, Func<CommitFailure, Exception> Reject)> _deferred = [];

    private readonly List<(Func<CommitFailure, bool> Matches, Func<CommitFailure, Task<CommitResolution?>> Decide)> _rules = [];

    /// <summary>A rule for the violation of <paramref name="constraint"/> (a unique index, a check or a foreign key, by name).</summary>
    public CommitRejectionMap On(string constraint, Func<CommitFailure, CommitResolution?> rule) =>
        OnAsync(constraint, failure => Task.FromResult(rule(failure)));

    /// <summary>The asynchronous form of <see cref="On"/>, for a rule that reads the storage back.</summary>
    public CommitRejectionMap OnAsync(string constraint, Func<CommitFailure, Task<CommitResolution?>> rule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(constraint);
        ArgumentNullException.ThrowIfNull(rule);
        _rules.Add((failure => failure.Exception is DbUpdateException && failure.Constraint == constraint, rule));
        return this;
    }

    /// <summary>Whether the module mapped a deferrable constraint, so its unit checks the deferred constraints before it commits.</summary>
    internal bool HasDeferredRules => _deferred.Count > 0;

    /// <summary>
    /// A rule for the violation of a <c>DEFERRABLE</c> <paramref name="constraint"/>, which Postgres reports only at commit.
    /// The unit checks it before committing (or releasing its savepoint), while the transaction still reads; the rule
    /// rejects and never repairs, since the save that wrote the rows already succeeded.
    /// </summary>
    public CommitRejectionMap OnDeferred<TRejection>(string constraint, Func<CommitFailure, TRejection> rule)
        where TRejection : Exception, ICommitRejection
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(constraint);
        ArgumentNullException.ThrowIfNull(rule);
        _deferred.Add((constraint, rule));
        return this;
    }

    /// <summary>A rule for a change another writer committed first (a row version that moved or a row that went away).</summary>
    public CommitRejectionMap OnConcurrency(Func<CommitFailure, CommitResolution?> rule) =>
        OnConcurrencyAsync(failure => Task.FromResult(rule(failure)));

    /// <summary>The asynchronous form of <see cref="OnConcurrency"/>.</summary>
    public CommitRejectionMap OnConcurrencyAsync(Func<CommitFailure, Task<CommitResolution?>> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _rules.Add((failure => failure.Exception is DbUpdateConcurrencyException, rule));
        return this;
    }

    /// <summary>A rule for any Postgres failure, such as a deadlock or a serialization failure, which names no constraint.</summary>
    public CommitRejectionMap OnPostgres(Func<CommitFailure, CommitResolution?> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _rules.Add((failure => failure.Postgres is not null, failure => Task.FromResult(rule(failure))));
        return this;
    }

    internal async Task<CommitResolution?> ResolveAsync(CommitFailure failure)
    {
        foreach (var (matches, decide) in _rules)
        {
            if (matches(failure) && await decide(failure) is { } resolution)
            {
                return resolution;
            }
        }

        return null;
    }

    /// <summary>The rejection of the first deferred rule that names the constraint the failure violated; none when no rule does.</summary>
    internal Exception? ResolveDeferred(CommitFailure failure)
    {
        foreach (var (constraint, reject) in _deferred)
        {
            if (failure.Constraint == constraint)
            {
                return reject(failure);
            }
        }

        return null;
    }
}
