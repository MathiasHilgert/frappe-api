namespace Frappe.Platform.Health;

/// <summary>The tags that select which health checks each probe endpoint runs.</summary>
public static class HealthTags
{
    /// <summary>Run by <c>/health/live</c>. Only the trivial <c>self</c> check carries it; never tag a dependency with it.</summary>
    public const string Live = "live";

    /// <summary>
    /// Run by <c>/health/startup</c> in addition to every critical dependency. Add it to a check that must pass once
    /// before the process takes traffic, such as a warm-up.
    /// </summary>
    public const string Startup = "startup";

    /// <summary>Added by <see cref="HealthExtensions.AddDependency{TCheck}"/> for <see cref="Criticality.Critical"/>; run by <c>/health/ready</c> and <c>/health/startup</c>.</summary>
    public const string Critical = "critical";

    /// <summary>Added by <see cref="HealthExtensions.AddDependency{TCheck}"/> for <see cref="Criticality.NonCritical"/>; run only by the detailed report.</summary>
    public const string NonCritical = "non_critical";
}
