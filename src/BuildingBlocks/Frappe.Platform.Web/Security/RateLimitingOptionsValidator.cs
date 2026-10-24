using Microsoft.Extensions.Options;

namespace Frappe.Platform.Web.Security;

/// <summary>Source-generated DataAnnotations validation for <see cref="RateLimitingOptions"/>, run when the host starts.</summary>
[OptionsValidator]
internal sealed partial class RateLimitingOptionsValidator : IValidateOptions<RateLimitingOptions>;
