using Microsoft.Extensions.Options;

namespace Frappe.Platform.Localization;

/// <summary>Source-generated DataAnnotations validation for <see cref="LocalizationOptions"/>, run when the host starts.</summary>
[OptionsValidator]
internal sealed partial class LocalizationOptionsValidator : IValidateOptions<LocalizationOptions>;
