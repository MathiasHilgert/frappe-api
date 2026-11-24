using Microsoft.Extensions.Options;

namespace Frappe.Identity.Infrastructure.Credentials;

/// <summary>Fails at startup, naming the full key, when a hashing parameter is out of range (<see cref="PasswordOptions"/>, CFG-03).</summary>
[OptionsValidator]
public sealed partial class PasswordOptionsValidator : IValidateOptions<PasswordOptions>;
