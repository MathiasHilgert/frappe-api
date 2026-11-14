namespace Frappe.Geo.Global.Places;

/// <summary>
/// List prices of the SKUs Geo's calls trigger (first volume tier, before the monthly free caps), in US dollars per call,
/// from https://developers.google.com/maps/billing-and-pricing/pricing (checked 2026-11-13). Assumptions: every
/// Autocomplete request bills "Autocomplete Requests" (a session ending with Place Details bills its first 12 requests
/// and the rest are free, an abandoned one bills them all, so this overestimates only sessions of 13+ keystrokes); a
/// resolution bills "Place Details Essentials", as Geo asks only for Essentials fields. Free caps and volume discounts
/// are not applied: the metric is an upper bound to watch trends, not an invoice.
/// </summary>
internal static class GooglePlacesPricing
{
    /// <summary>SKU "Autocomplete Requests": USD 2.83 per 1,000.</summary>
    public const double AutocompleteRequest = 2.83 / 1000;

    /// <summary>SKU "Place Details Essentials": USD 5.00 per 1,000.</summary>
    public const double PlaceDetailsEssentials = 5.00 / 1000;
}
