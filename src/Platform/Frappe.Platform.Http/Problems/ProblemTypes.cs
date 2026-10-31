namespace Frappe.Platform.Http.Problems;

/// <summary>
/// The stable problem <c>type</c> URNs clients branch on, each with its fixed English <c>title</c>.
/// </summary>
public static class ProblemTypes
{
    /// <summary>
    /// The ErrorOr numeric type of an error that means a malformed request, answered with 400; ErrorOr has no
    /// such type of its own.
    /// </summary>
    public const int MalformedRequestErrorType = 400;

    /// <summary>A business rule rejected the input (422).</summary>
    public const string Validation = "urn:frappe:problem:validation";

    /// <summary>The target does not exist or is not visible to the caller (404).</summary>
    public const string NotFound = "urn:frappe:problem:not_found";

    /// <summary>A business rule conflicts with the current state (409).</summary>
    public const string Conflict = "urn:frappe:problem:conflict";

    /// <summary>The caller may not perform the action (403).</summary>
    public const string Forbidden = "urn:frappe:problem:forbidden";

    /// <summary>The caller is not authenticated (401).</summary>
    public const string Unauthorized = "urn:frappe:problem:unauthorized";

    /// <summary>The caller sent too many requests (429).</summary>
    public const string RateLimited = "urn:frappe:problem:rate_limited";

    /// <summary>The request is not well formed (400, 405, 413 or 415).</summary>
    public const string MalformedRequest = "urn:frappe:problem:malformed_request";

    /// <summary>The request ran out of time (504).</summary>
    public const string Timeout = "urn:frappe:problem:timeout";

    /// <summary>The server failed unexpectedly (500).</summary>
    public const string Internal = "urn:frappe:problem:internal";

    internal static string TitleOf(string type) =>
        type switch
        {
            Validation => "The request failed validation.",
            NotFound => "The resource was not found.",
            Conflict => "The request conflicts with the current state.",
            Forbidden => "The action is not allowed.",
            Unauthorized => "Authentication is required.",
            RateLimited => "Too many requests.",
            MalformedRequest => "The request is malformed.",
            Timeout => "The request timed out.",
            _ => "The server failed to complete the request.",
        };
}
