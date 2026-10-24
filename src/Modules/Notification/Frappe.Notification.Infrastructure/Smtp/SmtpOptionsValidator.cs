using Microsoft.Extensions.Options;

namespace Frappe.Notification.Infrastructure.Smtp;

/// <summary>Source-generated DataAnnotations validation for <see cref="SmtpOptions"/>, run when the host starts.</summary>
[OptionsValidator]
internal sealed partial class SmtpOptionsValidator : IValidateOptions<SmtpOptions>;
