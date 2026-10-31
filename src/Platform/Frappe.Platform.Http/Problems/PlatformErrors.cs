namespace Frappe.Platform.Http.Problems;

/// <summary>
/// The errors the platform itself reports over HTTP, and the generic code of each problem type for a code that
/// ships without a text. They carry codes only: <c>errors.en.json</c> is the one English source of their texts.
/// </summary>
public static class PlatformErrors
{
    /// <summary>The body or a parameter could not be read (400).</summary>
    public static Error Malformed { get; } = MalformedRequest("platform.request.malformed");

    /// <summary>The body is larger than the endpoint accepts (413).</summary>
    public static Error TooLarge { get; } = MalformedRequest("platform.request.too_large");

    /// <summary>The body's content type is not one the endpoint reads (415).</summary>
    public static Error UnsupportedMediaType { get; } =
        MalformedRequest("platform.request.unsupported_media_type");

    /// <summary>The route exists but not for this HTTP method (405).</summary>
    public static Error MethodNotAllowed { get; } =
        MalformedRequest("platform.request.method_not_allowed");

    /// <summary>No route matches the request (404).</summary>
    public static Error RouteNotFound { get; } = Error.NotFound("platform.route.not_found");

    /// <summary>The request did not finish within its timeout (504).</summary>
    public static Error Timeout { get; } = Error.Unexpected("platform.request.timeout");

    /// <summary>The caller exceeded its rate limit (429).</summary>
    public static Error RateLimitExceeded { get; } =
        Error.Unexpected("platform.rate_limit.exceeded");

    /// <summary>The cursor does not decode, fails integrity or belongs to another sort or filter (400).</summary>
    public static Error InvalidCursor { get; } = MalformedRequest("platform.paging.invalid_cursor");

    /// <summary>The server failed; the only entry of every 500, whatever failed (500).</summary>
    public static Error Failed { get; } = Error.Unexpected("platform.request.failed");

    /// <summary>The generic text of a validation error whose code has none (422).</summary>
    public static Error Invalid { get; } = Error.Validation("platform.request.invalid");

    /// <summary>The generic text of a not-found error whose code has none (404).</summary>
    public static Error NotFound { get; } = Error.NotFound("platform.resource.not_found");

    /// <summary>The generic text of a conflict whose code has none (409).</summary>
    public static Error Conflict { get; } = Error.Conflict("platform.request.conflict");

    /// <summary>The generic text of a forbidden error whose code has none (403).</summary>
    public static Error Forbidden { get; } = Error.Forbidden("platform.request.forbidden");

    /// <summary>The generic text of an unauthorized error whose code has none (401).</summary>
    public static Error Unauthorized { get; } = Error.Unauthorized("platform.request.unauthorized");

    // ErrorOr has no malformed-request type and Custom needs a description: the code keeps logs on codes.
    private static Error MalformedRequest(string code) =>
        Error.Custom(ProblemTypes.MalformedRequestErrorType, code, code);
}
