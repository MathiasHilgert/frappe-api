namespace Frappe.Identity.Http.Owners;

/// <summary>The token the notice to the old address carried.</summary>
/// <param name="Token">The single-use token of the undo link.</param>
public sealed record UndoEmailChangeRequest(string? Token)
{
    /// <summary>Masked: the request holds a secret (DOM-06).</summary>
    public override string ToString() => "UndoEmailChangeRequest { the content is hidden }";
}
