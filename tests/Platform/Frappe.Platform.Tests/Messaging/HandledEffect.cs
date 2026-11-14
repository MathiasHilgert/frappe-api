namespace Frappe.Platform.Tests.Messaging;

/// <summary>What a test handler writes in the consumer's transaction.</summary>
public sealed class HandledEffect
{
    public Guid Id { get; set; }

    public string Step { get; set; } = string.Empty;
}
