namespace Frappe.UseCases;

/// <summary>A request for data that never changes state. Handled by exactly one <see cref="IQueryHandler{TQuery, TResult}"/>.</summary>
#pragma warning disable S2326 // CRAFT-10: the type parameter binds a request to its result type at compile time.
public interface IQuery<TResult>;
#pragma warning restore S2326
