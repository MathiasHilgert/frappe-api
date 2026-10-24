using Microsoft.Extensions.Options;

namespace Frappe.Platform.Caching;

/// <summary>Source-generated DataAnnotations validation for <see cref="ValkeyOptions"/>, run when the host starts.</summary>
[OptionsValidator]
internal sealed partial class ValkeyOptionsValidator : IValidateOptions<ValkeyOptions>;
