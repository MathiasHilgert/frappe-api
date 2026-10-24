using Frappe.SharedKernel.Validation;

namespace Frappe.Platform.Web.Security;

/// <summary>Failures the platform itself answers with, before any module code runs.</summary>
public static class PlatformErrors
{
    public static readonly Error AuthenticationRequired =
        new("platform.auth.required", "Authentication is required.", Kind: ErrorKind.Unauthorized);

    /// <summary>The in-process per-IP backstop refused the request; Retry-After says when to try again.</summary>
    public static readonly Error RateLimited =
        new("platform.rate_limited", "Too many requests. Try again later.", Kind: ErrorKind.TooManyRequests);

    public static readonly Error Forbidden =
        new("platform.auth.forbidden", "You are not allowed to do this.", Kind: ErrorKind.Forbidden);
}
