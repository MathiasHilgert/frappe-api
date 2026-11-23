namespace Frappe.Messaging.Hosting;

/// <summary>The consumers and steps a host registered; validated at startup so every step has its consumer.</summary>
internal sealed class ConsumerStepOptions
{
    public List<ConsumerStep> Steps { get; } = [];

    public List<ConsumerDeclaration> Consumers { get; } = [];
}
