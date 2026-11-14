namespace Frappe.Geo.Application.Addresses;

/// <summary>What Geo asks an address provider for.</summary>
public enum AddressOperation
{
    /// <summary>Suggestions while the user types.</summary>
    Suggest = 0,

    /// <summary>The chosen place's address.</summary>
    Resolve = 1,
}
