using Frappe.Results;

namespace Frappe.UseCases;

/// <summary>Inbound port for a query. Adapters depend on this interface, never on the implementation.</summary>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
