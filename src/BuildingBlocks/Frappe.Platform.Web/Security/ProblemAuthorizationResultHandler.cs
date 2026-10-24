using Frappe.Platform.Web.Problems;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Frappe.Platform.Web.Security;

/// <summary>
/// Lets the schemes challenge or forbid as usual (status, <c>WWW-Authenticate</c>), then writes the body every endpoint
/// failure has, through <see cref="ProblemResults.ToProblem"/>, instead of the bare status-page problem.
/// </summary>
internal sealed class ProblemAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _schemes = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        await _schemes.HandleAsync(next, context, policy, authorizeResult);
        if (context.Response.HasStarted)
            return;

        if (authorizeResult.Challenged)
            await Result.Failure(PlatformErrors.AuthenticationRequired).ToProblem().ExecuteAsync(context);
        else if (authorizeResult.Forbidden)
            await Result.Failure(PlatformErrors.Forbidden).ToProblem().ExecuteAsync(context);
    }
}
