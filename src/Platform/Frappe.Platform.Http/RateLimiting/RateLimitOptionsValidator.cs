using Microsoft.Extensions.Options;

namespace Frappe.Platform.Http.RateLimiting;

[OptionsValidator]
internal sealed partial class RateLimitOptionsValidator : IValidateOptions<RateLimitOptions>;
