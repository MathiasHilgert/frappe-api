namespace Frappe.Identity.Limits.Valkey;

/// <summary>
/// What an operator may change about the attempt limits (S5), bound from <c>Identity:Limits:&lt;flow&gt;</c>, for example
/// <c>Identity:Limits:code.guess:Limit</c>. Every flow has production defaults declared in code, and a setting here only tightens them: a
/// limit above the default or a window shorter than it is ignored, so a setting can never loosen a limit that guards a secret. What a flow
/// does while its counter is down is not configurable.
/// </summary>
public sealed class AttemptLimitOptions
{
    /// <summary>The section the options are bound from.</summary>
    public const string Section = "Identity:Limits";

    /// <summary>The longest window a setting may ask for: a month.</summary>
    public static readonly TimeSpan MaxWindow = TimeSpan.FromDays(30);

    /// <summary>The settings of each flow, by its name; a flow without an entry keeps its defaults.</summary>
    public Dictionary<string, FlowLimitOptions> Flows { get; } = [with(StringComparer.Ordinal)];
}
