using Microsoft.Extensions.Options;

namespace Frappe.Platform.Persistence;

[OptionsValidator]
internal sealed partial class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>;
