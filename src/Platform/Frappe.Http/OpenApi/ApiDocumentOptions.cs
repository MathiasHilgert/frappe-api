using System.ComponentModel.DataAnnotations;

namespace Frappe.Http.OpenApi;

/// <summary>
/// What the OpenAPI document says about the API as a whole (its <c>info</c>), bound from <see cref="Section"/>
/// (<c>Http:OpenApi:Title</c>, <c>Version</c>, <c>Description</c>); the host that serves the API owns the wording.
/// </summary>
public sealed class ApiDocumentOptions
{
    /// <summary>The configuration section the hosts bind.</summary>
    public const string Section = "Http:OpenApi";

    /// <summary>
    /// <c>Http:OpenApi:Title</c>: the API's name in the document and in the reference built from it. Default <c>API</c>.
    /// Range any non-empty text.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Configuration 'Http:OpenApi:Title' is required.")]
    public string Title { get; set; } = "API";

    /// <summary>
    /// <c>Http:OpenApi:Version</c>: the version of the contract, not of the build. Default <c>1.0.0</c>. Range any
    /// non-empty text; a client reads it to know which contract it was written against.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Configuration 'Http:OpenApi:Version' is required.")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// <c>Http:OpenApi:Description</c>: what the API is and for whom, in Markdown. Default empty: the document has no
    /// description.
    /// </summary>
    public string? Description { get; set; }
}
