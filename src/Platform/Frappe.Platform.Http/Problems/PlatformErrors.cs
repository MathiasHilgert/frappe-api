namespace Frappe.Platform.Http.Problems;

/// <summary>
/// The errors the platform itself reports over HTTP, and the generic text of each problem type for a code that
/// ships without one.
/// </summary>
public static class PlatformErrors
{
    /// <summary>The body or a parameter could not be read (400).</summary>
    public static Error Malformed { get; } =
        Error.Custom(
            ProblemTypes.MalformedRequestErrorType,
            "platform.request.malformed",
            "The request is malformed."
        );

    /// <summary>The body is larger than the endpoint accepts (413).</summary>
    public static Error TooLarge { get; } =
        Error.Custom(
            ProblemTypes.MalformedRequestErrorType,
            "platform.request.too_large",
            "The request body is too large."
        );

    /// <summary>The body's content type is not one the endpoint reads (415).</summary>
    public static Error UnsupportedMediaType { get; } =
        Error.Custom(
            ProblemTypes.MalformedRequestErrorType,
            "platform.request.unsupported_media_type",
            "The request content type is not supported."
        );

    /// <summary>The route exists but not for this HTTP method (405).</summary>
    public static Error MethodNotAllowed { get; } =
        Error.Custom(
            ProblemTypes.MalformedRequestErrorType,
            "platform.request.method_not_allowed",
            "The HTTP method is not allowed on this resource."
        );

    /// <summary>No route matches the request (404).</summary>
    public static Error RouteNotFound { get; } =
        Error.NotFound("platform.route.not_found", "No resource matches the request path.");

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

    /// <summary>The server failed; the only entry of every 500, whatever failed (500).</summary>
    public static Error Failed { get; } =
        Error.Unexpected("platform.request.failed", "The server failed to complete the request.");

    /// <summary>The generic text of a validation error whose code has none (422).</summary>
    public static Error Invalid { get; } =
        Error.Validation("platform.request.invalid", "The request is invalid.");

    /// <summary>The generic text of a not-found error whose code has none (404).</summary>
    public static Error NotFound { get; } =
        Error.NotFound("platform.resource.not_found", "The resource was not found.");

    /// <summary>The generic text of a conflict whose code has none (409).</summary>
    public static Error Conflict { get; } =
        Error.Conflict(
            "platform.request.conflict",
            "The request conflicts with the current state."
        );

    /// <summary>The generic text of a forbidden error whose code has none (403).</summary>
    public static Error Forbidden { get; } =
        Error.Forbidden("platform.request.forbidden", "The action is not allowed.");

    /// <summary>The generic text of an unauthorized error whose code has none (401).</summary>
    public static Error Unauthorized { get; } =
        Error.Unauthorized("platform.request.unauthorized", "Authentication is required.");
}
