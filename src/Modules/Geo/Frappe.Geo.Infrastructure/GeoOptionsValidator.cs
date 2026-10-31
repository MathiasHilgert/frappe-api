using Microsoft.Extensions.Options;

namespace Frappe.Geo.Infrastructure;

[OptionsValidator]
internal sealed partial class GeoOptionsValidator : IValidateOptions<GeoOptions>;
