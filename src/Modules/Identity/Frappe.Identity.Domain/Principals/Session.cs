namespace Frappe.Identity.Domain.Principals;

/// <summary>An opaque bearer credential. Only its hash is kept; expiry is checked on every read.</summary>
public sealed class Session
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(12);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(30);

    /// <summary>How long a step-up lasts from completion; use never extends it.</summary>
    public static readonly TimeSpan StepUpValidity = TimeSpan.FromMinutes(5);

    private Session() => TokenHash = null!;

    internal Session(string tokenHash, SessionSurface surface, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        TokenHash = tokenHash;
        Surface = surface;
        CreatedAt = now;
        LastSeenAt = now;
    }

    public Guid Id { get; }
    public string TokenHash { get; }
    public SessionSurface Surface { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset AbsoluteExpiresAt => CreatedAt + AbsoluteLifetime;

    /// <summary>Null until the person re-proves their password inside this session.</summary>
    public DateTimeOffset? StepUpExpiresAt { get; private set; }

    public bool IsLive(TimeProvider clock) => IsLive(CreatedAt, LastSeenAt, clock);

    /// <summary>The liveness rule on its own, for readers that check a session without loading its aggregate.</summary>
    public static bool IsLive(DateTimeOffset createdAt, DateTimeOffset lastSeenAt, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.GetUtcNow();
        return now < lastSeenAt + IdleTimeout && now < createdAt + AbsoluteLifetime;
    }

    /// <summary>Use pushes the idle expiry forward; nothing pushes the absolute one.</summary>
    internal void Touch(DateTimeOffset now) => LastSeenAt = now;

    internal DateTimeOffset StepUp(DateTimeOffset now)
    {
        var expiresAt = now + StepUpValidity;
        StepUpExpiresAt = expiresAt;
        return expiresAt;
    }

    internal bool HasFreshStepUp(TimeProvider clock) => IsLive(clock) && clock.GetUtcNow() < StepUpExpiresAt;
}
