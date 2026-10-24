using Microsoft.Extensions.Options;

namespace Frappe.Platform.Messaging.Sealing;

/// <summary>Source-generated DataAnnotations validation for <see cref="MessageSealOptions"/>, run when the host starts.</summary>
[OptionsValidator]
internal sealed partial class MessageSealOptionsValidator : IValidateOptions<MessageSealOptions>;
