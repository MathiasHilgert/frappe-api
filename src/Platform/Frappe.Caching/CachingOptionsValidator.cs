using Microsoft.Extensions.Options;

namespace Frappe.Caching;

/// <summary>Fails at startup, naming the full key, when the Valkey connection string is missing (<see cref="CachingOptions"/>).</summary>
[OptionsValidator]
public sealed partial class CachingOptionsValidator : IValidateOptions<CachingOptions>;
