using Frappe.Application.Abstractions.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Frappe.Platform.Realtime;

/// <summary>
/// Asks <see cref="IBusinessMembership"/> before the stream opens; a refusal answers 403 through the platform's
/// problem handler. A missing or malformed <c>businessId</c> passes here so binding answers 400: it reads nothing.
/// </summary>
internal sealed class BusinessMemberHandler(IBusinessMembership membership)
    : AuthorizationHandler<BusinessMemberRequirement, HttpContext>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, BusinessMemberRequirement requirement, HttpContext resource)
    {
        if (!Guid.TryParse(resource.Request.Query["businessId"], out var businessId)
            || await membership.IsMemberAsync(context.User, businessId, resource.RequestAborted))
        {
            context.Succeed(requirement);
        }
    }
}
