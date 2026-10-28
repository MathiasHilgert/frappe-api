using NodaTime;

namespace Frappe.Kernel.Messaging;

/// <summary>
/// Sends commands and queries to their handlers through the message pipeline, inline or deferred.
/// </summary>
/// <remarks>
/// Every method is generic over the concrete message type, so an implementation reads the
/// message's <c>static abstract</c> policy without reflection. Callers pass no type arguments:
/// both are inferred from the message's self-typed interface.
/// </remarks>
public interface IDispatcher
{
    /// <summary>Runs a command inline and returns its result.</summary>
    /// <typeparam name="TCommand">The command type; inferred from <paramref name="command"/>.</typeparam>
    /// <typeparam name="TResult">The value returned on success; inferred from <paramref name="command"/>.</typeparam>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result, or every expected failure.</returns>
    public Task<ErrorOr<TResult>> InvokeAsync<TCommand, TResult>(
        ICommand<TCommand, TResult> command,
        CancellationToken cancellationToken
    )
        where TCommand : ICommand<TCommand, TResult>;

    /// <summary>Runs a query inline and returns its result.</summary>
    /// <typeparam name="TQuery">The query type; inferred from <paramref name="query"/>.</typeparam>
    /// <typeparam name="TResult">The value returned on success; inferred from <paramref name="query"/>.</typeparam>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result, or every expected failure.</returns>
    public Task<ErrorOr<TResult>> QueryAsync<TQuery, TResult>(
        IQuery<TQuery, TResult> query,
        CancellationToken cancellationToken
    )
        where TQuery : IQuery<TQuery, TResult>;

    /// <summary>
    /// Stores a command durably to run as soon as possible, inside the current transaction when there is one.
    /// </summary>
    /// <typeparam name="TCommand">The command type; inferred from <paramref name="command"/>.</typeparam>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once the command is stored.</returns>
    public Task EnqueueAsync<TCommand>(TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand<TCommand>;

    /// <summary>Stores a command durably to run once at <paramref name="at"/>.</summary>
    /// <typeparam name="TCommand">The command type; inferred from <paramref name="command"/>.</typeparam>
    /// <param name="command">The command.</param>
    /// <param name="at">When the command runs.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes once the command is stored.</returns>
    public Task ScheduleAsync<TCommand>(
        TCommand command,
        Instant at,
        CancellationToken cancellationToken
    )
        where TCommand : ICommand<TCommand>;
}
