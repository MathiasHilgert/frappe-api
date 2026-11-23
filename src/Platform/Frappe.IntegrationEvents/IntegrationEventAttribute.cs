namespace Frappe.IntegrationEvents;

/// <summary>
/// The stable CloudEvents <c>type</c> of an integration event, for example <c>frappe.geo.holidays_changed.v1</c>:
/// <c>frappe.&lt;module&gt;.&lt;fact&gt;.v&lt;version&gt;</c>. It never changes once published.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationEventAttribute(string type) : Attribute
{
    public string Type { get; } = type;
}
