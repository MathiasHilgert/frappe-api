using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Credentials;

/// <summary>Fails at startup, naming the full key, when the breach check settings are missing or wrong (<see cref="BreachCheckOptions"/>).</summary>
[OptionsValidator]
public sealed partial class BreachCheckOptionsValidator : IValidateOptions<BreachCheckOptions>;
