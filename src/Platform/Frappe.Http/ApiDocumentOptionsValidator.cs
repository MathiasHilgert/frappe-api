using Microsoft.Extensions.Options;

namespace Frappe.Http;

/// <summary>Checks <see cref="ApiDocumentOptions"/> at startup: a document needs a title and a version.</summary>
internal sealed class ApiDocumentOptionsValidator : IValidateOptions<ApiDocumentOptions>
{
    public ValidateOptionsResult Validate(string? name, ApiDocumentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Title))
        {
            failures.Add($"Configuration '{ApiDocumentOptions.Section}:{nameof(ApiDocumentOptions.Title)}' must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.Version))
        {
            failures.Add($"Configuration '{ApiDocumentOptions.Section}:{nameof(ApiDocumentOptions.Version)}' must not be empty.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
