namespace Frappe.Platform.Health;

public static class HealthTags
{
    /// <summary>Checks run by the readiness probe.</summary>
    public const string Ready = "ready";

    public const string Critical = "critical";
    public const string NonCritical = "non-critical";
}
