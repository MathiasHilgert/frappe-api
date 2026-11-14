using Frappe.Geo.Application;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Http;

/// <summary>Runs a Geo command for an endpoint: a change that lost a race with another writer is a conflict, not a failure.</summary>
internal static class GeoCommands
{
    public static async Task<Result<TResult>> HandleAsync<TCommand, TResult>(ICommandHandler<TCommand, TResult> handler, TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        try
        {
            return await handler.HandleAsync(command, cancellationToken);
        }
        catch (ConcurrentChangeException)
        {
            return ConcurrentChangeException.Conflict;
        }
    }
}
