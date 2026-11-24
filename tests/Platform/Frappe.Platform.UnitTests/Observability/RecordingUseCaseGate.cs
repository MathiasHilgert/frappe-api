using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>
/// The use-case gate of the platform suites: it allows everything unless <see cref="Refusal"/> is set, and remembers the requests it saw and
/// where the unit of work stood at each call, so a test can pin the pipeline's order.
/// </summary>
public sealed class RecordingUseCaseGate(RecordingUnitOfWork? unitOfWork = null) : IUseCaseGate
{
    public Error? Refusal { get; set; }

    public List<string> Calls { get; } = [];

    public ValueTask<Error?> AdmitAsync(object request, CancellationToken cancellationToken)
    {
        Calls.Add($"admit {request.GetType().Name} after {unitOfWork?.Calls.Count ?? 0} unit-of-work calls");
        return ValueTask.FromResult(Refusal);
    }

    public ValueTask CompleteAsync(object request, bool succeeded, CancellationToken cancellationToken)
    {
        Calls.Add($"complete {request.GetType().Name} succeeded={succeeded} after {unitOfWork?.Calls.Count ?? 0} unit-of-work calls");
        return ValueTask.CompletedTask;
    }
}
