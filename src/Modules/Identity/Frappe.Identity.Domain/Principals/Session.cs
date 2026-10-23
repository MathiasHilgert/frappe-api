namespace Frappe.Identity.Domain.Principals;

/// <summary>An opaque bearer credential. Only its hash is kept; expiry is checked on every read.</summary>
public sealed class Session
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(12);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(30);

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

    public bool IsLive(TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        return now < LastSeenAt + IdleTimeout && now < AbsoluteExpiresAt;
    }

    /// <summary>Use pushes the idle expiry forward; nothing pushes the absolute one.</summary>
    internal void Touch(DateTimeOffset now) => LastSeenAt = now;
}
