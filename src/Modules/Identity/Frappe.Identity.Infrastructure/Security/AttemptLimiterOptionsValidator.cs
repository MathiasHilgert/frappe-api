using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Security;

/// <summary>Source-generated DataAnnotations validation for <see cref="AttemptLimiterOptions"/>, run when the host starts.</summary>
[OptionsValidator]
internal sealed partial class AttemptLimiterOptionsValidator : IValidateOptions<AttemptLimiterOptions>;
