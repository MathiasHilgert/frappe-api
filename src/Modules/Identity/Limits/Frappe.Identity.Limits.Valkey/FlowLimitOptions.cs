namespace Frappe.Identity.Limits.Valkey;

/// <summary>The settings of one limit flow (<see cref="AttemptLimitOptions"/>); each left unset keeps the flow's default.</summary>
public sealed class FlowLimitOptions
{
    /// <summary>
    /// <c>Identity:Limits:&lt;flow&gt;:Limit</c>: the attempts one subject may make per window, at least 1. Unset keeps the flow's default;
    /// a value above the default is ignored. Lowering it refuses people sooner.
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// <c>Identity:Limits:&lt;flow&gt;:Window</c>: how long a window lasts from its first attempt, a duration (<c>00:15:00</c>), above zero and
    /// at most 30 days. Unset keeps the flow's default; a value below the default is ignored. Lengthening it refuses people for longer.
    /// </summary>
    public TimeSpan? Window { get; set; }
}
