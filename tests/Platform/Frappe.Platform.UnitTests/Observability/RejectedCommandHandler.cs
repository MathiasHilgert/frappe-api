using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>Rejects its command with a business error.</summary>
public sealed class RejectedCommandHandler : ICommandHandler<RejectedCommand, Unit>
{
    public Task<Result<Unit>> HandleAsync(RejectedCommand command, CancellationToken cancellationToken) =>
        Task.FromResult<Result<Unit>>(Error.RuleViolation("test.rejected"));
}
