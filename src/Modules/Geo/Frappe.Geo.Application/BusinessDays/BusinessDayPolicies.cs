using Frappe.Geo.Domain.BusinessDays;

namespace Frappe.Geo.Application.BusinessDays;

/// <summary>The names of the business-day policies on the wire, in both directions, written once.</summary>
public static class BusinessDayPolicies
{
    private static readonly Dictionary<BusinessDayPolicy, string> Names = new()
    {
        [BusinessDayPolicy.Banking] = "banking",
        [BusinessDayPolicy.Labor] = "labor",
        [BusinessDayPolicy.Commercial] = "commercial",
    };

    private static readonly Dictionary<string, BusinessDayPolicy> Policies = Names.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);

    public static string NameOf(BusinessDayPolicy policy) => Names[policy];

    public static bool TryParse(string? name, out BusinessDayPolicy policy) =>
        Policies.TryGetValue(name?.Trim().ToLowerInvariant() ?? string.Empty, out policy);
}
