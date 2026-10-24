using Microsoft.AspNetCore.Authorization;

namespace Frappe.Platform.Realtime;

/// <summary>The caller must belong to the business named by the <c>businessId</c> query parameter.</summary>
internal sealed class BusinessMemberRequirement : IAuthorizationRequirement;
