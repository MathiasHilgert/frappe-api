using Frappe.Results;

namespace Frappe.UseCases;

/// <summary>Inbound port for a command. Adapters (HTTP, jobs, consumers) depend on this interface, never on the implementation.</summary>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
