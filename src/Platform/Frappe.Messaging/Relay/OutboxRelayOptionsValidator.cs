using Microsoft.Extensions.Options;

namespace Frappe.Messaging.Relay;

/// <summary>
/// Fails at startup, naming the key, on relay options that cannot work: the lease must outlive a whole pass plus one
/// publish and the settle, or a live relay could lose rows it is still publishing to another relay.
/// </summary>
[OptionsValidator]
public sealed partial class OutboxRelayOptionsValidator : IValidateOptions<OutboxRelayOptions>;
