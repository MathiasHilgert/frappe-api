namespace Frappe.Identity.Infrastructure.Sessions;

/// <summary>How long sessions live (S8), bound from <c>Identity:Sessions</c>. Both have defaults, so nothing needs to be set.</summary>
public sealed class SessionOptions
{
    /// <summary>The section the options are bound from.</summary>
    public const string Section = "Identity:Sessions";

    /// <summary>
    /// <c>Identity:Sessions:IdleTimeout</c>: how long a session may go unused before it ends, <c>hh:mm:ss</c> or <c>d.hh:mm:ss</c>. Default 12
    /// hours; positive and no longer than the absolute timeout. Applies to sessions opened or used after the change.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// <c>Identity:Sessions:AbsoluteTimeout</c>: how long after it opened a session ends however much it is used. Default 30 days; positive.
    /// Applies to sessions opened after the change.
    /// </summary>
    public TimeSpan AbsoluteTimeout { get; set; } = TimeSpan.FromDays(30);
}
