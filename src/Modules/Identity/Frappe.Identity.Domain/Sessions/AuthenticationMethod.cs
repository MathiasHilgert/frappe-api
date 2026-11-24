namespace Frappe.Identity.Domain.Sessions;

/// <summary>How the holder proved who they are when the session opened (the values of the <c>amr</c> claim).</summary>
public enum AuthenticationMethod
{
    /// <summary>A password.</summary>
    Pwd,
}
