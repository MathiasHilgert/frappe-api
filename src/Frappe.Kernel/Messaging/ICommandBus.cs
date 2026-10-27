using NodaTime;

namespace Frappe.Kernel.Messaging;

/// <summary>
/// Sends commands and queries to their handlers through the message pipeline.
/// </summary>
public interface ICommandBus
{
    /// <summary>Runs a command inline and returns its result.</summary>
    /// <typeparam name="TResult">The value returned on success.</typeparam>
    /// <param name="command">The command.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result, or every expected failure.</returns>
    public Task<ErrorOr<TResult>> InvokeAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken ct
    );

    /// <summary>Runs a query inline and returns its result.</summary>
    /// <typeparam name="TResult">The value returned on success.</typeparam>
    /// <param name="query">The query.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result, or every expected failure.</returns>
    public Task<ErrorOr<TResult>> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken ct);

    /// <summary>
    /// Stores a command durably to run as soon as possible, inside the current transaction when there is one.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes once the command is stored.</returns>
    public Task EnqueueAsync(ICommand command, CancellationToken ct);

    /// <summary>Stores a command durably to run once at <paramref name="at"/>.</summary>
    /// <param name="command">The command.</param>
    /// <param name="at">When the command runs.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes once the command is stored.</returns>
    public Task ScheduleAsync(ICommand command, Instant at, CancellationToken ct);
}
