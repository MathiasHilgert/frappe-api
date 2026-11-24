namespace Frappe.Access.Contracts;

/// <summary>
/// Marks a request whose handler must check a resource of type <typeparamref name="TResource"/> through
/// <see cref="IAuthorizer.RefuseAsync{T}"/> (the Pundit <c>verify_authorized</c> idea). When the handler succeeds without having made that
/// check, the use-case gate reports the mistake as an exception (500), never in silence. The gate learns it after the handler's transaction
/// committed, so it detects the mistake; it does not undo the write.
/// </summary>
#pragma warning disable S2326 // CRAFT-10: the type parameter binds a request to the resource it must check at compile time.
public interface IActsOn<TResource>
    where TResource : class;
#pragma warning restore S2326
