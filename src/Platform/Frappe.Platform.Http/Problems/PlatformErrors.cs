namespace Frappe.Platform.Http.Problems;

/// <summary>The errors the platform itself reports over HTTP.</summary>
public static class PlatformErrors
{
    /// <summary>The body or a parameter could not be read (400, or 413/415 for size and content type).</summary>
    public static Error Malformed { get; } =
        Error.Custom(
            ProblemTypes.MalformedRequestErrorType,
            "platform.request.malformed",
            "The request is malformed."
        );

    /// <summary>The request did not finish within its timeout (504).</summary>
    public static Error Timeout { get; } =
        Error.Unexpected("platform.request.timeout", "The request timed out.");

    /// <summary>The caller exceeded its rate limit (429).</summary>
    public static Error RateLimitExceeded { get; } =
        Error.Unexpected("platform.rate_limit.exceeded", "Too many requests; retry later.");

    /// <summary>The cursor does not decode, fails integrity or belongs to another sort or filter (400).</summary>
    public static Error InvalidCursor { get; } =
        Error.Custom(
            ProblemTypes.MalformedRequestErrorType,
            "platform.paging.invalid_cursor",
            "The cursor is invalid; start again from the first page."
        );

    /// <summary>The server failed unexpectedly (500).</summary>
    public static Error Failed { get; } =
        Error.Unexpected("platform.request.failed", "The server failed to complete the request.");
}
