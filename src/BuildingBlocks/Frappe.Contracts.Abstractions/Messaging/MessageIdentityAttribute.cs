namespace Frappe.Contracts.Abstractions.Messaging;

/// <summary>
/// The stable wire name of a message another module consumes, shaped <c>&lt;module&gt;.&lt;name&gt;.v&lt;n&gt;</c>, e.g.
/// <c>identity.owner_verification_code_issued.v1</c>. Persisted envelopes and queues carry this name instead of the CLR
/// type name, so renaming or moving the type never strands a message in flight. A breaking change to the shape is a
/// new message with the next version, consumed side by side until the old one drains.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MessageIdentityAttribute(string identity) : Attribute
{
    /// <summary>The wire name, e.g. <c>identity.owner_verification_code_issued.v1</c>.</summary>
    public string Identity { get; } = identity;
}
