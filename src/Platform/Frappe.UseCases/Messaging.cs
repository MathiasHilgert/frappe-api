using Frappe.Results;

namespace Frappe.UseCases;

/// <summary>An intent to change state. Handled by exactly one <see cref="ICommandHandler{TCommand, TResult}"/>.</summary>
public interface ICommand<TResult>;

/// <summary>A request for data that never changes state. Handled by exactly one <see cref="IQueryHandler{TQuery, TResult}"/>.</summary>
public interface IQuery<TResult>;

/// <summary>Inbound port for a command. Adapters (HTTP, jobs, consumers) depend on this interface, never on the implementation.</summary>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>Inbound port for a query. Adapters depend on this interface, never on the implementation.</summary>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
