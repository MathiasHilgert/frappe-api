namespace Frappe.Notifications.UnitTests.Emails;

/// <summary>The channel and kind plug-ins keep to the references DES-01 gives them.</summary>
public sealed class PluginLayeringTests
{
    private static readonly string[] Channels = ["Frappe.Notifications.Channels.Email"];

    private static readonly string[] Kinds = ["Frappe.Notifications.Kinds.Account", "Frappe.Notifications.Kinds.Marketing"];

    public static IEnumerable<string> Plugins() => [.. Channels, .. Kinds];

    [Test]
    [MethodDataSource(nameof(Plugins))]
    public async Task A_plugin_never_reaches_the_infrastructure_the_web_stack_or_another_kind(string assemblyName)
    {
        // Arrange
        var assembly = System.Reflection.Assembly.Load(assemblyName);

        // Act
        var forbidden = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Frappe.Notifications.Infrastructure", StringComparison.Ordinal)
                || name.StartsWith("Frappe.Notifications.Http", StringComparison.Ordinal)
                || (name.StartsWith("Frappe.Notifications.Kinds.", StringComparison.Ordinal) && name != assemblyName))
            .ToList();

        // Assert
        await Assert.That(forbidden).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(Plugins))]
    public async Task A_plugin_references_only_the_domain_the_application_the_platform_and_channels(string assemblyName)
    {
        // Arrange
        var assembly = System.Reflection.Assembly.Load(assemblyName);
        var isKind = Kinds.Contains(assemblyName);

        // Act
        var notificationReferences = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Frappe.Notifications.", StringComparison.Ordinal) && name != assemblyName)
            .Where(name => name != "Frappe.Notifications.Domain" && name != "Frappe.Notifications.Application")
            .Where(name => !(isKind && name.StartsWith("Frappe.Notifications.Channels.", StringComparison.Ordinal)))
            .ToList();

        // Assert
        await Assert.That(notificationReferences).IsEmpty();
    }
}
