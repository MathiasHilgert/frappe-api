using Microsoft.Extensions.Options;

namespace Frappe.Platform;

[OptionsValidator]
internal sealed partial class PlatformOptionsValidator : IValidateOptions<PlatformOptions>;
