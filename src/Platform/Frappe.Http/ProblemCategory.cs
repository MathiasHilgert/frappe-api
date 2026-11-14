namespace Frappe.Http;

/// <summary>
/// The kind of failure a Problem Details document answers, named by its <c>type</c>: <c>urn:frappe:problem:</c> and a
/// stable slug. The type never names an error code: the codes travel in <c>errors</c>, so a client branches on the
/// category and reads every error from one list, whether there is one or several.
/// </summary>
public sealed class ProblemCategory
{
    /// <summary>Prefix of every problem <c>type</c>.</summary>
    public const string TypePrefix = "urn:frappe:problem:";

    private ProblemCategory(string slug, int status, string titleKey)
    {
        Slug = slug;
        Status = status;
        TitleKey = titleKey;
    }

    /// <summary>A value of the request breaks its format or range (a use case's <c>InvalidValueError</c>).</summary>
    public static ProblemCategory InvalidValue { get; } = new("invalid-value", 400, ProblemTexts.InvalidValue);

    /// <summary>
    /// The framework could not read the request (a parameter it cannot bind, a malformed body). A request it rejects
    /// with another <c>4xx</c> (a <c>408</c> for a body arriving too slowly) keeps that status.
    /// </summary>
    public static ProblemCategory BadRequest { get; } = new("bad-request", 400, ProblemTexts.BadRequest);

    /// <summary>The resource does not exist (a use case's <c>NotFoundError</c>, or a URL no endpoint answers).</summary>
    public static ProblemCategory NotFound { get; } = new("not-found", 404, ProblemTexts.NotFound);

    /// <summary>The route exists but does not answer the method; <c>Allow</c> lists the ones it does.</summary>
    public static ProblemCategory MethodNotAllowed { get; } = new("method-not-allowed", 405, ProblemTexts.MethodNotAllowed);

    /// <summary>The operation conflicts with the current state (a use case's <c>ConflictError</c>).</summary>
    public static ProblemCategory Conflict { get; } = new("conflict", 409, ProblemTexts.Conflict);

    /// <summary>The request body is larger than the server accepts.</summary>
    public static ProblemCategory RequestTooLarge { get; } = new("request-too-large", 413, ProblemTexts.RequestTooLarge);

    /// <summary>The request body is in a media type the endpoint does not read; the error names the ones it does.</summary>
    public static ProblemCategory UnsupportedMediaType { get; } = new("unsupported-media-type", 415, ProblemTexts.UnsupportedMediaType);

    /// <summary>The operation breaks a business rule (a use case's <c>RuleViolationError</c>).</summary>
    public static ProblemCategory RuleViolation { get; } = new("rule-violation", 422, ProblemTexts.RuleViolation);

    /// <summary>The client spent its rate limit; <c>Retry-After</c> says when to try again.</summary>
    public static ProblemCategory TooManyRequests { get; } = new("too-many-requests", 429, ProblemTexts.RateLimited);

    /// <summary>The server failed; the exception is logged, never answered.</summary>
    public static ProblemCategory Unexpected { get; } = new("unexpected", 500, ProblemTexts.Unexpected);

    /// <summary>A dependency the operation needs is not configured or not answering (a use case's <c>UnavailableError</c>); the same request may succeed later.</summary>
    public static ProblemCategory Unavailable { get; } = new("unavailable", 503, ProblemTexts.Unavailable);

    /// <summary>Every category, in the order of their status.</summary>
    public static IReadOnlyList<ProblemCategory> All { get; } =
        [InvalidValue, BadRequest, NotFound, MethodNotAllowed, Conflict, RequestTooLarge, UnsupportedMediaType, RuleViolation, TooManyRequests, Unexpected, Unavailable];

    /// <summary>The stable name of the category, the end of its <see cref="Type"/>.</summary>
    public string Slug { get; }

    /// <summary>The HTTP status a problem of this category answers with (see <see cref="BadRequest"/> for the one exception).</summary>
    public int Status { get; }

    /// <summary>The problem <c>type</c>: <c>urn:frappe:problem:</c> and the <see cref="Slug"/>.</summary>
    public string Type => TypePrefix + Slug;

    /// <summary>Key of the localized <c>title</c>, one per category.</summary>
    internal string TitleKey { get; }
}
