namespace Frappe.Geo.Contracts.Queries;

/// <summary>How an address resolution ended.</summary>
public enum AddressStatus
{
    /// <summary>The address was validated and normalized.</summary>
    Resolved = 0,

    /// <summary>The reference is not one Geo issued.</summary>
    InvalidReference = 1,

    /// <summary>The provider no longer knows the place.</summary>
    NotFound = 2,

    /// <summary>The provider is not configured or did not answer: try again later; the stored snapshot still holds.</summary>
    Unavailable = 3,

    /// <summary>The language is not a BCP 47 tag.</summary>
    InvalidLanguage = 4,
}
