using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Frappe.Geo.IntegrationTests;

// Counts the commands the geo context sends, and keeps the readers' SQL and parameters; one counter per host, so
// parallel tests never add to each other.
internal sealed class CommandCounter : DbCommandInterceptor
{
    private readonly ConcurrentQueue<RecordedCommand> _readers = new();
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public IReadOnlyList<RecordedCommand> Readers => [.. _readers];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Interlocked.Increment(ref _count);
        _readers.Enqueue(
            new RecordedCommand(
                command.CommandText,
                [
                    .. command
                        .Parameters.Cast<DbParameter>()
                        .Select(static parameter => (parameter.ParameterName, parameter.Value)),
                ]
            )
        );
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Interlocked.Increment(ref _count);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default
    )
    {
        Interlocked.Increment(ref _count);
        return ValueTask.FromResult(result);
    }
}
