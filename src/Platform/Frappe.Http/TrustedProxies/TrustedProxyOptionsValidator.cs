using Microsoft.Extensions.Options;

namespace Frappe.Http.TrustedProxies;

/// <summary>Fails at startup, naming the key and the entry, on a proxy address or network that does not parse.</summary>
[OptionsValidator]
public sealed partial class TrustedProxyOptionsValidator : IValidateOptions<TrustedProxyOptions>;
