namespace Frappe.SharedKernel.Validation;

/// <summary>What kind of failure an <see cref="Error"/> is; the web edge maps it to a status code.</summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    TooManyRequests,
    Unauthorized,
    Forbidden,

    /// <summary>A dependency or capacity limit is temporarily exhausted; trying again later may succeed.</summary>
    Unavailable,
}
