using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>Throws an unexpected exception.</summary>
public sealed class ExplodingCommandHandler : ICommandHandler<ExplodingCommand, Unit>
{
    public Task<Result<Unit>> HandleAsync(ExplodingCommand command, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Infrastructure failure.");
}
