namespace Frappe.Identity.Http.Sessions;

/// <summary>What a signed-in caller gives to step their session up.</summary>
/// <param name="Password">The password the caller has now.</param>
public sealed record StepUpRequest(string? Password)
{
    /// <summary>Masked: the request holds a secret (DOM-06).</summary>
    public override string ToString() => "StepUpRequest { the content is hidden }";
}
