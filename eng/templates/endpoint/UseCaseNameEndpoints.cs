//#if (ModuleFirst)
using Frappe.ModuleName.Application.AreaName.UseCaseName;
using Frappe.Http;
//#else
using Frappe.Http;
using Frappe.ModuleName.Application.AreaName.UseCaseName;
//#endif
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Frappe.ModuleName.Http.AreaName;

/// <summary>
/// FILL IN: describe MapVerb /route-placeholder: who may call it, its answers and its refusals. It requires a session; the principal comes from it, never from the URL. Never cached.
/// </summary>
internal static class UseCaseNameEndpoints
{
    extension(RouteGroupBuilder group)
    {
        public void MapUseCaseNameEndpoints() =>
            group.MapVerb("/route-placeholder", HandleAsync)
                .WithName("UseCaseName").RequireSession().NoStore()
                .WithSummary("FILL IN: summary")
                .WithDescription("FILL IN: description")
                .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

//#if (IsQuery)
    private static async Task<Results<Ok<Unit>, ErrorsProblemHttpResult>> HandleAsync(
        IQueryHandler<UseCaseName, Unit> handler, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var principal = SessionPolicy.RequiredPrincipalIdOf(httpContext.User);
        return (await handler.HandleAsync(new UseCaseName(principal), cancellationToken)).ToTypedHttpResult(result => TypedResults.Ok(result));
    }
//#else
    private static async Task<Results<NoContent, ErrorsProblemHttpResult>> HandleAsync(
        ICommandHandler<UseCaseName, Unit> handler, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var principal = SessionPolicy.RequiredPrincipalIdOf(httpContext.User);
        return (await handler.HandleAsync(new UseCaseName(principal), cancellationToken)).ToTypedHttpResult(_ => TypedResults.NoContent());
    }
//#endif
}
