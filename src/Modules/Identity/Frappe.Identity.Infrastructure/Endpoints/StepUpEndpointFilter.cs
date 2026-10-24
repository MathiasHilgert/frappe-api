using Frappe.Identity.Application.Account;
using Frappe.Identity.Application.Sessions;
using Frappe.Identity.Domain;
using Frappe.Platform.Web.Problems;
using Frappe.SharedKernel.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Frappe.Identity.Infrastructure.Endpoints;

internal static class StepUpEndpointFilter
{
    /// <summary>
    /// Demands a step-up completed on the caller's session within the last five minutes, answering 403
    /// <c>identity.step_up.required</c> otherwise. Put it after <c>RequireAuthorization</c>; the client then calls
    /// <c>POST /v1/identity/sessions/current/step-ups</c> and retries.
    /// </summary>
    public static TBuilder RequireStepUp<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(InvokeAsync);

    internal static async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var caller = services.GetRequiredService<ICurrentPrincipal>().RequiredSession();
        var bus = services.GetRequiredService<IMessageBus>();
        return await bus.InvokeAsync<bool>(new HasFreshStepUp(caller.PrincipalId, caller.SessionId), context.HttpContext.RequestAborted)
            ? await next(context)
            : Result.Failure(IdentityErrors.StepUpRequired).ToProblem();
    }
}
