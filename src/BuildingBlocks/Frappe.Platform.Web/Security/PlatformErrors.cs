using Frappe.SharedKernel.Validation;

namespace Frappe.Platform.Web.Security;

/// <summary>Failures the platform itself answers with, before any module code runs.</summary>
public static class PlatformErrors
{
    public static readonly Error AuthenticationRequired =
        new("platform.auth.required", "Authentication is required.", Kind: ErrorKind.Unauthorized);

    public static readonly Error Forbidden =
        new("platform.auth.forbidden", "You are not allowed to do this.", Kind: ErrorKind.Forbidden);
}
