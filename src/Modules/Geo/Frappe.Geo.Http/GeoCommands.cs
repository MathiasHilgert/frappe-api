using Frappe.Geo.Application;
using Frappe.Geo.Application.Territory;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Http;

/// <summary>Runs a Geo command for an endpoint: a change that lost a race with another writer is a conflict (or, for an alias, what the command run second answers), not a failure.</summary>
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
        catch (SubdivisionAliasRaceLostException exception)
        {
            return exception.Answer;
        }
    }
}
