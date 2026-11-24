namespace Frappe.Messaging.Abstractions;

/// <summary>What a module declared about its messaging: a fact recorded in the service collection, with no transport behind it.</summary>
internal interface IMessagingDeclaration
{
    /// <summary>Hands the declaration, with its generic arguments, to the transport that materializes it.</summary>
    void Accept(IMessagingDeclarationVisitor visitor);
}
