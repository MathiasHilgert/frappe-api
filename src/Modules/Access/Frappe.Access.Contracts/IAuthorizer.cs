using Frappe.Results;

namespace Frappe.Access.Contracts;

/// <summary>
/// Checks the caller against a loaded resource. A handler makes exactly one call, after loading (CQRS-03), and answers the refusal when there
/// is one. The precedence is hidden (the resource's own not-found), then forbidden, then step-up; a missing book, an undeclared action or a rule
/// that throws is hidden. Access never answers 401: authentication stays in Identity.
/// </summary>
public interface IAuthorizer
{
    /// <summary>
    /// Nothing when the caller may do <paramref name="action"/> on <paramref name="resource"/>; otherwise the error to answer: a
    /// <see cref="NotFoundError"/> with the resource's own code, or a <see cref="ForbiddenError"/> (<see cref="AccessErrorCodes"/>).
    /// </summary>
    ValueTask<Error?> RefuseAsync<T>(ResourceAction<T> action, T resource, CancellationToken cancellationToken)
        where T : class;
}
