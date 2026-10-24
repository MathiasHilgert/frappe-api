using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Security;

/// <summary>Source-generated DataAnnotations validation for <see cref="SecretHashingOptions"/>, run when the host starts.</summary>
[OptionsValidator]
internal sealed partial class SecretHashingOptionsValidator : IValidateOptions<SecretHashingOptions>;
