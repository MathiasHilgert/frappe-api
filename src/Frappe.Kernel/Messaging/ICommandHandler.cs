namespace Frappe.Kernel.Messaging;

/// <summary>
/// Handles <typeparamref name="TCommand"/>; only the dispatcher calls it.
/// </summary>
/// <typeparam name="TCommand">The command handled.</typeparam>
/// <typeparam name="TResult">The value returned on success.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TCommand, TResult>
{
    /// <summary>Runs the use case.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result, or every expected failure.</returns>
    public Task<ErrorOr<TResult>> Handle(TCommand command, CancellationToken cancellationToken);
}
