using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Frappe.Persistence;

/// <summary>A save the storage refused, with what a module's rule needs to tell why and to read the storage back.</summary>
/// <param name="Context">The unit's context, its failed save already rolled back to EF's savepoint (so it still reads).</param>
/// <param name="Exception">What the save threw.</param>
/// <param name="CancellationToken">The command's token, for the reads a rule makes.</param>
public sealed record CommitFailure(DbContext Context, Exception Exception, CancellationToken CancellationToken)
{
    /// <summary>The Postgres failure behind the exception (the exception itself or its inner one); none for any other.</summary>
    public PostgresException? Postgres => Exception as PostgresException ?? Exception.InnerException as PostgresException;

    /// <summary>The constraint a unique, check or foreign key violation names; none for any other failure.</summary>
    public string? Constraint => Postgres?.ConstraintName;
}
