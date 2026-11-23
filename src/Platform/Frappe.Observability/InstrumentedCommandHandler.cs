using Frappe.Results;
using Frappe.UseCases;
using Microsoft.Extensions.Logging;

namespace Frappe.Observability;

/// <summary>Wraps a command handler with a span, a duration measurement and an outcome tag.</summary>
internal sealed class InstrumentedCommandHandler<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    HandlerMetrics metrics,
    ILogger<InstrumentedCommandHandler<TCommand, TResult>> logger) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken) =>
        HandlerInstrumentation.ExecuteAsync(
            "command",
            typeof(TCommand),
            metrics,
            logger,
            () => inner.HandleAsync(command, cancellationToken),
            cancellationToken);
}
