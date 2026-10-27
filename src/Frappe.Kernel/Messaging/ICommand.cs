using Frappe.Kernel.Authorization;

namespace Frappe.Kernel.Messaging;

/// <summary>
/// A request to change state that returns <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TResult">The value the handler returns on success.</typeparam>
public interface ICommand<TResult>
{
    /// <summary>Gets the policy the caller must satisfy.</summary>
    public static abstract Policy Policy { get; }
}

/// <summary>
/// A command with no result; the only kind of command that can be enqueued or scheduled.
/// </summary>
public interface ICommand : ICommand<Success>;
