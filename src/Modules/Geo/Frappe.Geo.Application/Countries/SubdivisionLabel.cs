namespace Frappe.Geo.Application.Countries;

/// <summary>What a country calls its first-level subdivision: the state_name_type keys of Google's libaddressinput.</summary>
public enum SubdivisionLabel
{
    /// <summary>An area.</summary>
    Area,

    /// <summary>A county.</summary>
    County,

    /// <summary>A department.</summary>
    Department,

    /// <summary>A district.</summary>
    District,

    /// <summary>A do or si, as in South Korea.</summary>
    DoSi,

    /// <summary>An emirate.</summary>
    Emirate,

    /// <summary>An island.</summary>
    Island,

    /// <summary>An oblast.</summary>
    Oblast,

    /// <summary>A parish.</summary>
    Parish,

    /// <summary>A prefecture.</summary>
    Prefecture,

    /// <summary>A province, as in Argentina.</summary>
    Province,

    /// <summary>A state.</summary>
    State,
}
