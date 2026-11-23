using Microsoft.Extensions.Options;

namespace Frappe.Health;

/// <summary>Fails at startup, naming the full key of every probe port outside 1 to 65535 (<see cref="TcpProbeOptions"/>).</summary>
[OptionsValidator]
public sealed partial class TcpProbeOptionsValidator : IValidateOptions<TcpProbeOptions>;
