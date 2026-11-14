using Frappe.Geo.Domain.Holidays;

namespace Frappe.Geo.Countries.Argentina.Banking;

/// <summary>
/// The banking sector's own non-working days, as data. 6 November, Bank Workers' Day, comes from the bank employees'
/// collective agreement (CCT 18/75, art. 50) and is absent from the BCRA's list. Whether interbank clearing stops on it
/// is unverified: the rule says bank staff do not work, which is what the banking calendar records.
/// </summary>
internal static class BankingSectorRules
{
    public static IReadOnlyList<BankingSectorRule> All { get; } =
    [
        new(11, 6, "Día del Bancario", new LegalBasis("CCT 18/75, art. 50", null)),
    ];
}
