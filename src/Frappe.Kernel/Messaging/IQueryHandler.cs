namespace Frappe.Kernel.Messaging;

/// <summary>
/// Handles <typeparamref name="TQuery"/>; only the command bus calls it.
/// </summary>
/// <typeparam name="TQuery">The query handled.</typeparam>
/// <typeparam name="TResult">The value returned on success.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    /// <summary>Runs the query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result, or every expected failure.</returns>
    public Task<ErrorOr<TResult>> Handle(TQuery query, CancellationToken ct);
}
