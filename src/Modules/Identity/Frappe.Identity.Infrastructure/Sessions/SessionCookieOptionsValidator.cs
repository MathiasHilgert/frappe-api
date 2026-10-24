using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Sessions;

/// <summary>Source-generated DataAnnotations validation for <see cref="SessionCookieOptions"/>, run when the host starts.</summary>
[OptionsValidator]
internal sealed partial class SessionCookieOptionsValidator : IValidateOptions<SessionCookieOptions>;
