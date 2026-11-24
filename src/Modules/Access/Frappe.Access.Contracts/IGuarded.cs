namespace Frappe.Access.Contracts;

/// <summary>
/// Marks a request (a command or a query) that only some callers may run, and makes its author declare which: the compiler refuses a guarded
/// request with no <see cref="Allowed"/>. The use-case gate evaluates it before the handler runs. A request carries exactly one marker,
/// <see cref="IGuarded"/> or <see cref="IPublic"/>.
/// </summary>
public interface IGuarded
{
    /// <summary>Who may run the request, written next to it: <c>Is.Authenticated &amp; Has.RecentStepUp</c>.</summary>
    static abstract CallerRule Allowed { get; }
}
