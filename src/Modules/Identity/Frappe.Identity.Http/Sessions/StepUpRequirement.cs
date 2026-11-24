using Frappe.Http;
using Frappe.Identity.Domain;
using Frappe.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Identity.Http.Sessions;

/// <summary>
/// Guards a route that only a recent password check allows (S14): the session must have stepped up and its window must still be open by the
/// clock of the request. The claim is the instant as the request authenticated, so it is compared with the clock here, never trusted alone.
/// </summary>
internal static class StepUpRequirement
{
    extension<TBuilder>(TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>
        /// Requires an open step-up window on top of the session the route already asks for: without one it answers 403
        /// <c>identity.step_up.required</c> and the handler never runs. Put it after <c>RequireSession</c>.
        /// </summary>
        public TBuilder RequireStepUp()
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.AddEndpointFilter(RefuseWithoutStepUpAsync).ProducesProblem(StatusCodes.Status403Forbidden);
        }
    }

    private static ValueTask<object?> RefuseWithoutStepUpAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var validUntil = SessionPolicy.StepUpValidUntilOf(http.User);
        if (validUntil is { } until && until > http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow())
        {
            return next(context);
        }

        Result<Unit> required = new ForbiddenError(IdentityErrorCodes.StepUpRequired);
        return ValueTask.FromResult<object?>(required.ToTypedHttpResult(_ => TypedResults.NoContent()));
    }
}
