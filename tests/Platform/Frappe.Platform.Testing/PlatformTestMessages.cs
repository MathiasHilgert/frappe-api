using System.Reflection;

namespace Frappe.Platform.Testing;

/// <summary>The assembly holding the error messages the Platform suites register with <c>AddErrorMessages</c>.</summary>
public static class PlatformTestMessages
{
    /// <summary>The assembly whose embedded <c>Resources/Messages</c> files the Platform tests share.</summary>
    public static Assembly Assembly { get; } = typeof(PlatformTestMessages).Assembly;
}
