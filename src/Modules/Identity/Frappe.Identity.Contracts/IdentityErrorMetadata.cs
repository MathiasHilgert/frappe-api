namespace Frappe.Identity.Contracts;

/// <summary>
/// Keys of the facts an Identity error discloses in its metadata; the web edge writes each one as a top-level member
/// of the problem body, so these names are part of the HTTP contract.
/// </summary>
public static class IdentityErrorMetadata
{
    /// <summary>The owner's id, disclosed only after the password was proven, so the console can route to verify.</summary>
    public const string OwnerId = "ownerId";
}
