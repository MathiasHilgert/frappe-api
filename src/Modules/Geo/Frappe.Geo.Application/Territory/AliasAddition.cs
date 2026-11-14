namespace Frappe.Geo.Application.Territory;

/// <summary>How staging a manual alias ended.</summary>
public enum AliasAdditionOutcome
{
    /// <summary>Staged; the unit of work saves it.</summary>
    Added,

    /// <summary>The subdivision is already known by it: its name, its code's suffix or one of its aliases.</summary>
    Duplicate,

    /// <summary>Another current subdivision of the country is known by it (<see cref="AliasAddition.OtherSubdivisionCode"/>).</summary>
    Taken,
}

/// <summary>The outcome of staging a manual alias; for <see cref="AliasAdditionOutcome.Taken"/>, the subdivision known by it.</summary>
public sealed record AliasAddition(AliasAdditionOutcome Outcome, string? OtherSubdivisionCode = null);

/// <summary>How staging the removal of an alias ended.</summary>
public enum AliasRemoval
{
    /// <summary>A manual alias, staged for removal.</summary>
    Removed,

    /// <summary>The source's alias: it changes only with the source.</summary>
    Imported,

    /// <summary>The subdivision has no such alias.</summary>
    NotFound,
}
