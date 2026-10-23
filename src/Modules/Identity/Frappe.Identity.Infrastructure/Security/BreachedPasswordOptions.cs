namespace Frappe.Identity.Infrastructure.Security;

/// <summary>Where the HaveIBeenPwned range API lives and how long a sign-up waits for it before failing open.</summary>
public sealed class BreachedPasswordOptions
{
    public const string Section = "Identity:BreachedPasswords";
    public const int MinimumTimeoutMilliseconds = 100;
    public const int MaximumTimeoutMilliseconds = 10_000;

    public Uri BaseAddress { get; set; } = new("https://api.pwnedpasswords.com/");
    public int TimeoutMilliseconds { get; set; } = 1_500;

    public bool IsValid() =>
        BaseAddress.IsAbsoluteUri
        && TimeoutMilliseconds is >= MinimumTimeoutMilliseconds and <= MaximumTimeoutMilliseconds;
}
