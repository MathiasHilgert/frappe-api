using Frappe.Kernel.Authorization;

namespace Frappe.Kernel.Messaging;

/// <summary>
/// A request to read state that returns <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TSelf">The implementing query type, so the dispatcher can read its policy statically.</typeparam>
/// <typeparam name="TResult">The value the handler returns on success.</typeparam>
public interface IQuery<TSelf, TResult>
    where TSelf : IQuery<TSelf, TResult>
{
    /// <summary>Gets the policy the caller must satisfy.</summary>
    static abstract Policy Policy { get; }
}
