using Microsoft.Extensions.Options;

namespace Frappe.Messaging.JetStream;

/// <summary>
/// Fails at startup, naming the key, on consumer options too small for a safe heartbeat (<see cref="JetStreamConsumerOptions"/>).
/// </summary>
[OptionsValidator]
public sealed partial class JetStreamConsumerOptionsValidator : IValidateOptions<JetStreamConsumerOptions>;
