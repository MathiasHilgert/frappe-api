using Microsoft.Extensions.Options;

namespace Frappe.Notification.Infrastructure.Resend;

/// <summary>Source-generated DataAnnotations validation for <see cref="ResendOptions"/>, run when the host starts.</summary>
[OptionsValidator]
internal sealed partial class ResendOptionsValidator : IValidateOptions<ResendOptions>;
