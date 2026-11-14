using Frappe.Geo.Domain.Holidays;

namespace Frappe.Geo.Countries.Argentina.Banking;

/// <summary>A day the banking sector does not work every year by its own agreement, not by the BCRA's list.</summary>
internal sealed record BankingSectorRule(int Month, int Day, string Name, LegalBasis LegalBasis);
