using Microsoft.Extensions.Options;

namespace Frappe.Http;

/// <summary>Fails at startup, naming the key and the entry, on a console origin that is not one (<see cref="ConsoleOriginOptions"/>).</summary>
[OptionsValidator]
internal sealed partial class ConsoleOriginOptionsValidator : IValidateOptions<ConsoleOriginOptions>;
