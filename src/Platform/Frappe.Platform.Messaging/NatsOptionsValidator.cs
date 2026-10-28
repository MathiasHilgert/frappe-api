using Microsoft.Extensions.Options;

namespace Frappe.Platform.Messaging;

[OptionsValidator]
internal sealed partial class NatsOptionsValidator : IValidateOptions<NatsOptions>;
