using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Observability;

/// <summary>
/// Asks the <see cref="IUseCaseGate"/> before a query runs and answers its refusal as a failed Result; see
/// <see cref="GatedCommandHandler{TCommand, TResult}"/>.
/// </summary>
internal sealed class GatedQueryHandler<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner,
    IUseCaseGate gate) : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    public async Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken)
    {
        if (await gate.AdmitAsync(query, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var succeeded = false;
        try
        {
            var result = await inner.HandleAsync(query, cancellationToken);
            succeeded = result.IsSuccess;
            return result;
        }
        finally
        {
            await gate.CompleteAsync(query, succeeded, cancellationToken);
        }
    }
}
