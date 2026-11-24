using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Identity.IntegrationTests;

/// <summary>
/// The use-case gate of the Identity persistence suites: they test what the handlers do against a database, not who may run them (that is
/// Access's and the route suites' business), so every request is admitted.
/// </summary>
internal sealed class AllowAllUseCaseGate : IUseCaseGate
{
    public ValueTask<Error?> AdmitAsync(object request, CancellationToken cancellationToken) => ValueTask.FromResult<Error?>(null);

    public ValueTask CompleteAsync(object request, bool succeeded, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
