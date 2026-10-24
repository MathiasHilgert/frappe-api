using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Security;

/// <summary>Source-generated DataAnnotations validation for <see cref="Argon2Options"/>, run when the host starts.</summary>
[OptionsValidator]
internal sealed partial class Argon2OptionsValidator : IValidateOptions<Argon2Options>;
