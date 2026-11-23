using Microsoft.Extensions.Options;

namespace Frappe.Messaging.JetStream;

/// <summary>Fails at startup, naming the full key, when the NATS connection string is missing (<see cref="JetStreamOptions"/>).</summary>
[OptionsValidator]
public sealed partial class JetStreamOptionsValidator : IValidateOptions<JetStreamOptions>;
