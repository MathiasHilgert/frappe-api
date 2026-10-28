using Frappe.Kernel.Messaging;

namespace Frappe.Platform.IntegrationTests.Messaging.Kitchen.Application;

public sealed class SweepTicketsHandler : ICommandHandler<SweepTickets, Success>
{
    public Task<ErrorOr<Success>> Handle(SweepTickets command, CancellationToken cancellationToken)
    {
        Runs.Record(command.RunId);
        return Task.FromResult<ErrorOr<Success>>(Result.Success);
    }
}
