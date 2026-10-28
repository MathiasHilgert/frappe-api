using Microsoft.Extensions.Options;

namespace Frappe.Platform.Hosting;

[OptionsValidator]
internal sealed partial class PlatformOptionsValidator : IValidateOptions<PlatformOptions>;
