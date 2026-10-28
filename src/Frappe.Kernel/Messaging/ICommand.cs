using Frappe.Kernel.Authorization;

namespace Frappe.Kernel.Messaging;

/// <summary>
/// A request to change state that returns <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TSelf">The implementing command type, so the dispatcher can read its policy statically.</typeparam>
/// <typeparam name="TResult">The value the handler returns on success.</typeparam>
public interface ICommand<TSelf, TResult>
    where TSelf : ICommand<TSelf, TResult>
{
    /// <summary>Gets the policy the caller must satisfy.</summary>
    public static abstract Policy Policy { get; }
}

/// <summary>
/// A command with no result; the only kind of command that can be enqueued or scheduled.
/// </summary>
/// <typeparam name="TSelf">The implementing command type, so the dispatcher can read its policy statically.</typeparam>
public interface ICommand<TSelf> : ICommand<TSelf, Success>
    where TSelf : ICommand<TSelf>;
