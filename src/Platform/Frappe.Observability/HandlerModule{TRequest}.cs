namespace Frappe.Observability;

/// <summary>The module of <typeparamref name="TRequest"/>, worked out once per request type instead of on every call.</summary>
/// <typeparam name="TRequest">The command or query type.</typeparam>
internal static class HandlerModule<TRequest>
{
    /// <summary>The module name in lower case, as <see cref="HandlerModule.Of"/> gives it.</summary>
    public static readonly string Name = HandlerModule.Of(typeof(TRequest));
}
