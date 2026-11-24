using Frappe.Access.Contracts;
using Frappe.Results;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// The authorizer of the Identity persistence suites: they test what the handlers do against a database, not who may act on a resource (the
/// rules are Access's and the route suites' business), so every check allows.
/// </summary>
internal sealed class AllowAllAuthorizer : IAuthorizer
{
    public ValueTask<Error?> RefuseAsync<T>(ResourceAction<T> action, T resource, CancellationToken cancellationToken)
        where T : class => ValueTask.FromResult<Error?>(null);
}
