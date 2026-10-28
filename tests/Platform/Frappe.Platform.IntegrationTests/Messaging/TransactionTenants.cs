using System.Collections.Concurrent;
using System.Data.Common;
using Frappe.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Frappe.Platform.IntegrationTests.Messaging;

// Records the tenant open when each fixture context begins its transaction: the transaction's first statement
// sets the tenant for row-level security, so the tenant must be known by then.
internal sealed class TransactionTenants : DbTransactionInterceptor
{
    public ConcurrentQueue<(Type Context, Guid? Tenant)> Begun { get; } = new();

    public override InterceptionResult<DbTransaction> TransactionStarting(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result
    )
    {
        Record(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection,
        TransactionStartingEventData eventData,
        InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default
    )
    {
        Record(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Record(DbContext? context) =>
        Begun.Enqueue((context?.GetType() ?? typeof(DbContext), TenantScope.Current?.BusinessId));
}
