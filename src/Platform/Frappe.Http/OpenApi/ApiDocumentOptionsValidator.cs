using Microsoft.Extensions.Options;

namespace Frappe.Http.OpenApi;

/// <summary>Fails at startup, naming the key, when the document has no title or no version (<see cref="ApiDocumentOptions"/>).</summary>
[OptionsValidator]
public sealed partial class ApiDocumentOptionsValidator : IValidateOptions<ApiDocumentOptions>;
