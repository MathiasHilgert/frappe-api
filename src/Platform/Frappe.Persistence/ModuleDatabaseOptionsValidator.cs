using Microsoft.Extensions.Options;

namespace Frappe.Persistence;

/// <summary>
/// Fails at startup, naming the full key of the module, when a module's connection string is missing
/// (<see cref="ModuleDatabaseOptions"/>).
/// </summary>
[OptionsValidator]
public sealed partial class ModuleDatabaseOptionsValidator : IValidateOptions<ModuleDatabaseOptions>;
