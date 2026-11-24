namespace Frappe.Identity.Infrastructure.Credentials;

/// <summary>
/// How passwords are hashed (S4), bound from <c>Identity:Passwords</c>. The defaults are OWASP's Argon2id minimum (19 MiB, 2 passes, 1 lane);
/// the lane count is fixed at 1 by the library. Hashes made under other values keep verifying and are replaced on the next log-in.
/// </summary>
public sealed class PasswordOptions
{
    /// <summary>The section the options are bound from.</summary>
    public const string Section = "Identity:Passwords";

    /// <summary>The most memory a hash may be set to, in mebibytes; it bounds what one log-in attempt costs the server.</summary>
    public const int MaxMemoryMebibytes = 256;

    /// <summary>
    /// <c>Identity:Passwords:Iterations</c>: Argon2id passes over the memory. Default 2, range 1 to 10. Raising it slows every hash and
    /// verification and makes every stored hash due for replacement at the next log-in.
    /// </summary>
    [OptionRange("Identity:Passwords:Iterations", 1, 10)]
    public int Iterations { get; set; } = 2;

    /// <summary>
    /// <c>Identity:Passwords:MemoryMebibytes</c>: memory one hash uses, in mebibytes. Default 19, range 8 to 256. Raising it costs
    /// that memory per concurrent log-in and makes stored hashes due for replacement at the next log-in.
    /// </summary>
    [OptionRange("Identity:Passwords:MemoryMebibytes", 8, MaxMemoryMebibytes)]
    public int MemoryMebibytes { get; set; } = 19;
}
