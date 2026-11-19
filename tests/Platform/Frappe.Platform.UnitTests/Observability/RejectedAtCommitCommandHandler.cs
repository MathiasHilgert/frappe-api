using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>Succeeds, and the commit that follows is rejected.</summary>
public sealed class RejectedAtCommitCommandHandler : ICommandHandler<RejectedAtCommitCommand, Unit>
{
    public Task<Result<Unit>> HandleAsync(RejectedAtCommitCommand command, CancellationToken cancellationToken) =>
        throw new LostRaceException();
}
