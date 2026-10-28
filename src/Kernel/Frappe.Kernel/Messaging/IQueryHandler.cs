namespace Frappe.Kernel.Messaging;

/// <summary>
/// Handles <typeparamref name="TQuery"/>; only the dispatcher calls it.
/// </summary>
/// <typeparam name="TQuery">The query handled.</typeparam>
/// <typeparam name="TResult">The value returned on success.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TQuery, TResult>
{
    /// <summary>Runs the query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result, or every expected failure.</returns>
    Task<ErrorOr<TResult>> Handle(TQuery query, CancellationToken cancellationToken);
}
