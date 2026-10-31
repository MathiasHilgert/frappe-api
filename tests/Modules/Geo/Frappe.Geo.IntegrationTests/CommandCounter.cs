using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Frappe.Geo.IntegrationTests;

// Counts the commands a context sends, so a test sees query shape that a correct payload hides. The tenant
// set_config each transaction opens with is the Platform's, not the read's, so it does not count.
internal sealed class CommandCounter : DbCommandInterceptor
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public override DbCommand CommandInitialized(CommandEndEventData eventData, DbCommand result)
    {
        if (!result.CommandText.Contains("set_config", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _count);
        }

        return result;
    }
}
