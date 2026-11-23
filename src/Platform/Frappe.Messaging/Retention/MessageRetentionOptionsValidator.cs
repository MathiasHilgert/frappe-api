using Microsoft.Extensions.Options;

namespace Frappe.Messaging.Retention;

/// <summary>
/// Fails at startup, naming the key, on a retention that would delete what is still needed or cannot be computed
/// (<see cref="MessageRetentionOptions"/>).
/// </summary>
[OptionsValidator]
public sealed partial class MessageRetentionOptionsValidator : IValidateOptions<MessageRetentionOptions>;
