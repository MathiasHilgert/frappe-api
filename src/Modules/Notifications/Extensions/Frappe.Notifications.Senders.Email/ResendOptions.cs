namespace Frappe.Notifications.Senders.Email;

/// <summary>
/// How the Resend adapter calls the provider, bound from <c>Notifications:Email:Resend</c>. The API key is a secret and comes only from
/// the environment (<c>Notifications__Email__Resend__ApiKey</c>, CFG-10).
/// </summary>
public sealed class ResendOptions
{
    /// <summary>The section the options are bound from.</summary>
    public const string Section = "Notifications:Email:Resend";

    /// <summary>Resend's published limit is 10 requests per second per team; the adapter stays below it.</summary>
    public const int ProviderRequestsPerSecond = 10;

    /// <summary>
    /// <c>Notifications:Email:Resend:ApiKey</c>: the Resend API key. No default: required when <c>Notifications:Email:Sender</c> is
    /// <c>Resend</c>, ignored otherwise. An empty value means unset. Never logged.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// <c>Notifications:Email:Resend:BaseUrl</c>: where the API lives. Default <c>https://api.resend.com/</c>. Range an absolute https URL
    /// ending in a slash. Only a test or a proxy changes it.
    /// </summary>
    public Uri BaseUrl { get; set; } = new("https://api.resend.com/");

    /// <summary>
    /// <c>Notifications:Email:Resend:RequestsPerSecond</c>: the rate the adapter allows itself, as a token bucket. Default 9. Range 1 to 9,
    /// below Resend's limit of 10. Raising it risks 429s that are retried; lowering it slows a burst of sends. The budget applies per Worker
    /// process, outside the retries, and holds because the Worker runs one replica with a consumer prefetch of 1.
    /// </summary>
    public int RequestsPerSecond { get; set; } = 9;

    /// <summary>
    /// <c>Notifications:Email:Resend:AttemptTimeout</c>: the longest one HTTP attempt may take. Default 8 s. Range above zero.
    /// </summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// <c>Notifications:Email:Resend:TotalTimeout</c>: the longest a send may take with every retry. Default 30 s. Range longer than
    /// <see cref="AttemptTimeout"/>. The consumer's <c>MaxHandlerDuration</c> must stay above it.
    /// </summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// <c>Notifications:Email:Resend:Retries</c>: retries of one send after a 429, a 5xx or a timeout, before the message is redelivered
    /// by the broker. Default 3. Range 1 to 10. Every attempt carries the same idempotency key.
    /// </summary>
    public int Retries { get; set; } = 3;

    /// <summary>
    /// <c>Notifications:Email:Resend:RetryDelay</c>: the base wait between retries, doubled each time with jitter. Default 500 ms. Range
    /// zero or more (zero is for tests).
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);
}
