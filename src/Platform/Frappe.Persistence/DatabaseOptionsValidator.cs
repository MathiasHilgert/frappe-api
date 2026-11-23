using Microsoft.Extensions.Options;

namespace Frappe.Persistence;

/// <summary>
/// Fails, naming each key, on database limits that cannot work: a pool of fewer than 1 connection (or more than
/// <see cref="DatabaseOptions.MaxPoolSizeLimit"/>), a command or statement timeout that is not whole seconds or whole
/// milliseconds between 1 s and 1 h, and an idle-in-transaction timeout under
/// <see cref="DatabaseOptions.MinIdleInTransactionSessionTimeout"/>. Zero, which both Npgsql and Postgres read as
/// "no limit", is refused on purpose: the point of these settings is that nothing waits forever.
/// </summary>
[OptionsValidator]
public sealed partial class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>;
