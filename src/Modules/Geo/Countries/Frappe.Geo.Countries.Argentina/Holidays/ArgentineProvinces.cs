namespace Frappe.Geo.Countries.Argentina.Holidays;

/// <summary>Calendarios Nacionales' province slugs and the ISO 3166-2 code of each: the 23 provinces and the city of Buenos Aires.</summary>
internal static class ArgentineProvinces
{
    public static readonly IReadOnlyDictionary<string, string> Codes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["salta"] = "AR-A",
        ["buenos-aires"] = "AR-B",
        ["caba"] = "AR-C",
        ["san-luis"] = "AR-D",
        ["entre-rios"] = "AR-E",
        ["la-rioja"] = "AR-F",
        ["santiago-del-estero"] = "AR-G",
        ["chaco"] = "AR-H",
        ["san-juan"] = "AR-J",
        ["catamarca"] = "AR-K",
        ["la-pampa"] = "AR-L",
        ["mendoza"] = "AR-M",
        ["misiones"] = "AR-N",
        ["formosa"] = "AR-P",
        ["neuquen"] = "AR-Q",
        ["rio-negro"] = "AR-R",
        ["santa-fe"] = "AR-S",
        ["tucuman"] = "AR-T",
        ["chubut"] = "AR-U",
        ["tierra-del-fuego"] = "AR-V",
        ["corrientes"] = "AR-W",
        ["cordoba"] = "AR-X",
        ["jujuy"] = "AR-Y",
        ["santa-cruz"] = "AR-Z",
    };
}
