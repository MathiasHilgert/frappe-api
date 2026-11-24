using Frappe.Results;

namespace Frappe.UseCases;

/// <summary>
/// The seam every command and query passes through before its handler and after it, owned by whoever decides who may run what (the Access
/// module). The platform knows no rule: it asks the gate, answers a refusal as a failed result and lets the gate check what the handler did.
/// A host that registers no gate fails when it resolves a handler, so a use case can never run unguarded by omission.
/// </summary>
public interface IUseCaseGate
{
    /// <summary>
    /// Before the handler: the error to answer instead of running it, or nothing to let it run. A refusal needs no <see cref="CompleteAsync"/>;
    /// every other answer does, whatever the handler does.
    /// </summary>
    ValueTask<Error?> AdmitAsync(object request, CancellationToken cancellationToken);

    /// <summary>
    /// After the handler ran (<paramref name="succeeded"/> tells how it ended): the gate checks what the request owes. It throws when a request
    /// succeeded without keeping its obligations, so the mistake is reported instead of passing in silence. It runs after the handler's
    /// transaction committed: it detects the mistake, it does not undo the write.
    /// </summary>
    ValueTask CompleteAsync(object request, bool succeeded, CancellationToken cancellationToken);
}
