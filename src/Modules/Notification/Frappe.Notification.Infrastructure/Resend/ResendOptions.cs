using System.ComponentModel.DataAnnotations;

namespace Frappe.Notification.Infrastructure.Resend;

/// <summary>Resend API settings. The key is a secret: set it through the environment, never in a committed file.</summary>
internal sealed class ResendOptions : IValidatableObject
{
    public const string SectionName = "Notification:Resend";

    public string? ApiKey { get; set; }

    [Required]
    public string From { get; set; } = "Frappé <no-reply@frappe.la>";

    [Required]
    public Uri BaseAddress { get; set; } = new("https://api.resend.com/");

    /// <summary>Per attempt. Short, because the durable queue owns retries; a hung call must not hold a worker.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:02:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// True only in Development, where mail goes to Mailpit or the log. Set by the module from the environment, never
    /// bound from configuration (it is not public), so no setting can switch the key off in production.
    /// </summary>
    internal bool ApiKeyOptional { get; set; }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BaseAddress is not { IsAbsoluteUri: true })
            yield return new ValidationResult($"'{SectionName}:BaseAddress' must be absolute.", [nameof(BaseAddress)]);
        if (!HasApiKey && !ApiKeyOptional)
            yield return new ValidationResult($"'{SectionName}:ApiKey' is required outside Development.", [nameof(ApiKey)]);
    }
}
