namespace Frappe.Identity.Infrastructure.Security;

/// <summary>
/// Argon2id cost. Defaults are the OWASP minimum (19 MiB, 2 passes, 1 lane); the host refuses to start below it.
/// </summary>
public sealed class Argon2Options
{
    public const string Section = "Identity:PasswordHashing";
    public const int MinimumMemorySizeKib = 19_456;
    public const int MinimumIterations = 2;

    public int MemorySizeKib { get; set; } = MinimumMemorySizeKib;
    public int Iterations { get; set; } = MinimumIterations;
    public int Parallelism { get; set; } = 1;

    public bool IsValid() =>
        MemorySizeKib >= MinimumMemorySizeKib && Iterations >= MinimumIterations && Parallelism is >= 1 and <= 16;
}
