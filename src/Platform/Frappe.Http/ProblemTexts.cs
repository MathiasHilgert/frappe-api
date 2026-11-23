namespace Frappe.Http;

/// <summary>Keys of the texts the platform itself renders in Problem Details.</summary>
internal static class ProblemTexts
{
    public const string InvalidValue = "problem.invalid_value";
    public const string RuleViolation = "problem.rule_violation";
    public const string NotFound = "problem.not_found";
    public const string Unauthenticated = "problem.unauthenticated";
    public const string Forbidden = "problem.forbidden";
    public const string Conflict = "problem.conflict";
    public const string Several = "problem.several";
    public const string RateLimited = "problem.rate_limited";
    public const string MethodNotAllowed = "problem.method_not_allowed";
    public const string BadRequest = "problem.bad_request";
    public const string RequestTooLarge = "problem.request_too_large";
    public const string UnsupportedMediaType = "problem.unsupported_media_type";
    public const string Unexpected = "problem.unexpected";
    public const string Unavailable = "problem.unavailable";
}
