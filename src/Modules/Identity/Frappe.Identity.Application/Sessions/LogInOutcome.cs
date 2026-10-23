using Frappe.SharedKernel.Validation;

namespace Frappe.Identity.Application.Sessions;

/// <summary>What a sign-in attempt came to. Only an attempt that proved the password learns the account's state.</summary>
public abstract record LogInOutcome
{
    private LogInOutcome()
    {
    }

    public sealed record Opened(Guid PrincipalId, Guid SessionId, string Token, DateTimeOffset ExpiresAt) : LogInOutcome;

    /// <summary>Unknown email, malformed email or wrong password: deliberately indistinguishable.</summary>
    public sealed record InvalidCredentials : LogInOutcome;

    public sealed record Throttled(TimeSpan RetryAfter) : LogInOutcome;

    /// <summary>The right password for an account the domain refuses to open a session for (pending or suspended).</summary>
    public sealed record Refused(Guid PrincipalId, Error Error) : LogInOutcome;
}
