using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Frappe.Geo.IntegrationTests;

// Counts the commands the geo context sends; one counter per host, so parallel tests never add to each other.
internal sealed class CommandCounter : DbCommandInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Interlocked.Increment(ref _count);
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
