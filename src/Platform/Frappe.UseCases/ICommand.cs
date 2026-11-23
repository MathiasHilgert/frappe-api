namespace Frappe.UseCases;

/// <summary>An intent to change state. Handled by exactly one <see cref="ICommandHandler{TCommand, TResult}"/>.</summary>
#pragma warning disable S2326 // CRAFT-10: the type parameter binds a request to its result type at compile time.
public interface ICommand<TResult>;
#pragma warning restore S2326
