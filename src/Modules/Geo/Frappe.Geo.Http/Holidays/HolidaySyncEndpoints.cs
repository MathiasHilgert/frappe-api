using System.Globalization;
using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Application.Holidays.ListHolidaySyncRuns;
using Frappe.Geo.Application.Holidays.ListHolidaySyncs;
using Frappe.Geo.Application.Holidays.RequestHolidaySync;
using Frappe.Geo.Http.Territory;
using Frappe.Http;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NodaTime;
using NodaTime.Text;

namespace Frappe.Geo.Http.Holidays;

/// <summary>holiday-syncs (S12): the holiday sources, their audited runs, and a run asked for by hand.</summary>
internal static class HolidaySyncEndpoints
{
    public static void MapHolidaySyncEndpoints(this RouteGroupBuilder geo)
    {
#pragma warning disable S1135 // The TODO is tracked: authentication is out of v1's scope (S17).
        // TODO(auth): administration only; open while Frappé has no authentication (S17).
        geo.MapGet("/holiday-syncs", ListAsync)
            .WithName("ListHolidaySyncs")
            .WithSummary("Holiday sources")
            .WithDescription("Every holiday source that ran at least once, with how its last run ended. Never cached (no-store).");

        // TODO(auth): administration only; open while Frappé has no authentication (S17).
        geo.MapGet("/holiday-syncs/{sourceId}/runs", ListRunsAsync)
            .WithName("ListHolidaySyncRuns")
            .WithSummary("Runs of a holiday source")
            .WithDescription($"The source's latest {ListHolidaySyncRuns.Limit} runs, newest first, with the override conflicts each one found; 404 for a source that is not a holiday source. Never cached (no-store).")
            .ProducesProblem(StatusCodes.Status404NotFound);

        // TODO(auth): administration only; open while Frappé has no authentication (S17).
        geo.MapPost("/holiday-syncs/{sourceId}/runs", RequestAsync)
            .WithName("RequestHolidaySync")
            .WithSummary("Run a holiday source now")
            .WithDescription("Asks the Worker to run the source besides its schedule; 202 with the request (requestId) and the absolute path of the source's runs in Location, where the run appears once done. It starts within seconds (never alongside another run of the same source); requests made before it starts are run once.")
            .ProducesProblem(StatusCodes.Status404NotFound);
#pragma warning restore S1135
    }

    private static async Task<Results<Ok<HolidaySyncListResponse>, ErrorsProblemHttpResult>> ListAsync(
        IQueryHandler<ListHolidaySyncs, IReadOnlyList<HolidaySyncView>> handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListHolidaySyncs(), cancellationToken)).ToTypedHttpResult(list => TypedResults.Ok(
            new HolidaySyncListResponse([.. list.Select(s => new HolidaySyncResponse(s.SourceId, Format(s.LastAttemptAt), Wire(s.LastOutcome), Format(s.LastSuccessAt)))])));

    private static async Task<Results<Ok<SyncRunListResponse>, ErrorsProblemHttpResult>> ListRunsAsync(
        string sourceId, IQueryHandler<ListHolidaySyncRuns, IReadOnlyList<SyncRunView>> handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListHolidaySyncRuns(sourceId), cancellationToken)).ToTypedHttpResult(runs => TypedResults.Ok(
            new SyncRunListResponse([.. runs.Select(ToResponse)])));

    /// <summary>The Location is the absolute path of the source's runs: the request has no resource of its own, and the run lands there.</summary>
    private static async Task<Results<Accepted<HolidaySyncRequestResponse>, ErrorsProblemHttpResult>> RequestAsync(
        string sourceId, ICommandHandler<RequestHolidaySync, SyncRequestView> handler, LinkGenerator links, HttpContext httpContext, CancellationToken cancellationToken) =>
        (await GeoCommands.HandleAsync(handler, new RequestHolidaySync(sourceId), cancellationToken)).ToTypedHttpResult(request => TypedResults.Accepted(
            links.GetPathByName(httpContext, "ListHolidaySyncRuns", new { sourceId = request.SourceId }),
            new HolidaySyncRequestResponse(request.Id.ToString(), request.SourceId, Format(request.RequestedAt)!)));

    private static SyncRunResponse ToResponse(SyncRunView run) => new(
        run.Id.ToString(),
        Format(run.StartedAt)!,
        Format(run.FinishedAt)!,
        Wire(run.Outcome)!,
        run.Inserted,
        run.Updated,
        run.Error,
        [.. run.Conflicts.Select(c => new OverrideConflictResponse(c.Jurisdiction, c.Year, LocalDatePattern.Iso.Format(c.Date), c.Name))]);

    private static string? Format(Instant? instant) => instant is { } value ? InstantPattern.ExtendedIso.Format(value) : null;

    /// <summary>An outcome as stored (<c>NotPublished</c>) on the wire (<c>not_published</c>).</summary>
    private static string? Wire(string? outcome) =>
        outcome is null ? null : string.Concat(outcome.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + Lower(c) : Lower(c)));

    private static string Lower(char c) => char.ToLower(c, CultureInfo.InvariantCulture).ToString();
}
