using Frappe.Results;
using Frappe.UseCases;
using Microsoft.Extensions.Logging;

namespace Frappe.Observability;

/// <summary>Wraps a query handler with a span, a duration measurement and an outcome tag.</summary>
internal sealed class InstrumentedQueryHandler<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner,
    HandlerMetrics metrics,
    ILogger<InstrumentedQueryHandler<TQuery, TResult>> logger) : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    public Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken) =>
        HandlerInstrumentation.ExecuteAsync(
            "query",
            typeof(TQuery),
            metrics,
            logger,
            () => inner.HandleAsync(query, cancellationToken),
            cancellationToken);
}
