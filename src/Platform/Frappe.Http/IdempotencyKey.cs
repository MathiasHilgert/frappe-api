namespace Frappe.Http;

/// <summary>The platform's <c>Idempotency-Key</c> (HTTP-26): its header, limits and error codes.</summary>
public static class IdempotencyKey
{
    /// <summary>The request header a client sends on a creation it may retry.</summary>
    public const string HeaderName = "Idempotency-Key";

    /// <summary>The response header of an answer that is a replay, never the first answer.</summary>
    public const string ReplayedHeaderName = "Idempotent-Replayed";

    /// <summary>The longest key accepted, in characters; its characters are printable ASCII without spaces.</summary>
    public const int MaxLength = 255;

    /// <summary>How long the first answer is kept and replayed.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>
    /// How long a claim of a request still running holds the key: longer than any request lasts, short enough that a
    /// replica that died mid-request does not block its retries for the whole <see cref="Retention"/>.
    /// </summary>
    public static readonly TimeSpan InFlightLifetime = TimeSpan.FromMinutes(2);

    /// <summary>The key is empty, longer than <see cref="MaxLength"/>, not printable ASCII, or sent more than once.</summary>
    public const string InvalidCode = "platform.idempotency_key.invalid";

    /// <summary>The key was used for a request with other content.</summary>
    public const string ReusedCode = "platform.idempotency_key.reused";

    /// <summary>The first request with this key is still running.</summary>
    public const string InProgressCode = "platform.idempotency_key.in_progress";

    /// <summary>The store that keeps the answers did not answer, so the request is not run: it could not be retried safely.</summary>
    public const string UnavailableCode = "platform.idempotency.unavailable";
}
