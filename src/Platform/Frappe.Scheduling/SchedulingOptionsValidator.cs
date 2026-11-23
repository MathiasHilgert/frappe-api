using Microsoft.Extensions.Options;

namespace Frappe.Scheduling;

/// <summary>Fails at startup, naming the full key, when the job store's connection string is missing (<see cref="SchedulingOptions"/>).</summary>
[OptionsValidator]
public sealed partial class SchedulingOptionsValidator : IValidateOptions<SchedulingOptions>;
