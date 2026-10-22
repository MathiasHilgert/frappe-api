namespace Frappe.Platform.Health;

public enum DependencyCriticality
{
    /// <summary>Without it the instance cannot serve: readiness fails (503) and traffic is drained.</summary>
    Critical,

    /// <summary>The instance degrades but keeps serving: readiness reports Degraded and stays 200.</summary>
    NonCritical,
}
