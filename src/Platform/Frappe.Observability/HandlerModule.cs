namespace Frappe.Observability;

/// <summary>The module a command or query belongs to, from the namespace convention <c>Frappe.&lt;Module&gt;.*</c>.</summary>
internal static class HandlerModule
{
    /// <summary>The module name in lower case; <c>unknown</c> outside the convention.</summary>
    public static string Of(Type requestType)
    {
        var segments = requestType.Namespace?.Split('.') ?? [];
        return segments.Length > 1 ? segments[1].ToLowerInvariant() : "unknown";
    }
}
