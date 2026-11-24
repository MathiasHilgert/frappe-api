using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Observability;

/// <summary>
/// Asks the <see cref="IUseCaseGate"/> before a command runs and answers its refusal as a failed Result: the handler never starts and no unit
/// of work begins (the gate sits outside the transaction and inside the span, so a denial is still traced). After the handler it tells the
/// gate how it ended.
/// </summary>
internal sealed class GatedCommandHandler<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IUseCaseGate gate) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        if (await gate.AdmitAsync(command, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var succeeded = false;
        try
        {
            var result = await inner.HandleAsync(command, cancellationToken);
            succeeded = result.IsSuccess;
            return result;
        }
        finally
        {
            await gate.CompleteAsync(command, succeeded, cancellationToken);
        }
    }
}
