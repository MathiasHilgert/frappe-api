using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Notifications.IntegrationTests;

/// <summary>
/// The use-case gate of the Notifications persistence suites: they test what the handlers do against a database, not who may run them (that is
/// Access's business), so every request is admitted.
/// </summary>
internal sealed class AllowAllUseCaseGate : IUseCaseGate
{
    public ValueTask<Error?> AdmitAsync(object request, CancellationToken cancellationToken) => ValueTask.FromResult<Error?>(null);

    public ValueTask CompleteAsync(object request, bool succeeded, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
