using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>Accepts its command.</summary>
public sealed class AcceptedCommandHandler : ICommandHandler<AcceptedCommand, string>
{
    public Task<Result<string>> HandleAsync(AcceptedCommand command, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string>>($"accepted:{command.Marker}");
}
