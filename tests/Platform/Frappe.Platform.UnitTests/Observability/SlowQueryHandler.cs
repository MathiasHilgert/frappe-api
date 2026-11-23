using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>Waits until the caller cancels.</summary>
public sealed class SlowQueryHandler : IQueryHandler<SlowQuery, string>
{
    public async Task<Result<string>> HandleAsync(SlowQuery query, CancellationToken cancellationToken)
    {
        await new TaskCompletionSource().Task.WaitAsync(cancellationToken);
        return "never";
    }
}
