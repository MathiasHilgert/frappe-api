namespace Frappe.Http;

/// <summary>What the API document says about the API itself: the host sets it, the platform has no text of its own.</summary>
public sealed class ApiDocumentOptions
{
    /// <summary>The configuration section: <c>Api:Document</c>.</summary>
    public const string Section = "Api:Document";

    /// <summary><c>Api:Document:Title</c>: the document's <c>info.title</c>. Defaults to <c>API</c>.</summary>
    public string Title { get; set; } = "API";

    /// <summary><c>Api:Document:Version</c>: the document's <c>info.version</c>, the version of the API contract. Defaults to <c>1.0.0</c>.</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary><c>Api:Document:Description</c>: the document's <c>info.description</c>, what the API is and for whom. Defaults to empty, which omits it.</summary>
    public string Description { get; set; } = string.Empty;
}
