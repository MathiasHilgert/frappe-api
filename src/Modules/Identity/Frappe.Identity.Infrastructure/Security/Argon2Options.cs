using System.ComponentModel.DataAnnotations;

namespace Frappe.Identity.Infrastructure.Security;

/// <summary>
/// Argon2id cost. Defaults are the OWASP minimum (19 MiB, 2 passes, 1 lane); the host refuses to start below it.
/// </summary>
internal sealed class Argon2Options
{
    public const string Section = "Identity:PasswordHashing";
    public const int MinimumMemorySizeKib = 19_456;
    public const int MinimumIterations = 2;

    // The generated Range validator ignores ErrorMessage, so failures name the member: Argon2Options.MemorySizeKib.
    [Range(MinimumMemorySizeKib, int.MaxValue)]
    public int MemorySizeKib { get; set; } = MinimumMemorySizeKib;

    [Range(MinimumIterations, int.MaxValue)]
    public int Iterations { get; set; } = MinimumIterations;

    [Range(1, 16)]
    public int Parallelism { get; set; } = 1;

    /// <summary>
    /// How many hashes may run at once; the rest wait for a slot. Each one holds <see cref="MemorySizeKib"/> and a
    /// core, so a burst of sign-ins queues instead of exhausting memory and starving the thread pool.
    /// </summary>
    [Range(1, 1_024)]
    public int MaxConcurrentHashes { get; set; } = Environment.ProcessorCount;
}
