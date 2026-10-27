using Frappe.Kernel.Authorization;

namespace Frappe.Kernel.Messaging;

/// <summary>
/// A request to read state that returns <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TResult">The value the handler returns on success.</typeparam>
public interface IQuery<TResult>
{
    /// <summary>Gets the policy the caller must satisfy.</summary>
    public static abstract Policy Policy { get; }
}
