using System.Globalization;

namespace Frappe.Kernel.Currencies;

// TODO: Money takes its MinorUnit from geo's ICurrencyCatalogue when Money arrives (no Money type yet)

/// <summary>The number of decimal digits of a currency's minor unit, from 0 (CLP) to 4 (CLF).</summary>
public sealed record MinorUnit
{
    private const int MaxDigits = 4;

    private MinorUnit(int value) => Value = value;

    /// <summary>Gets the error returned for a digit count outside 0 to 4.</summary>
    public static Error Invalid { get; } =
        Error.Validation(
            "kernel.minor_unit.invalid",
            "The minor unit must be between 0 and 4 decimal digits."
        );

    /// <summary>Gets the number of decimal digits.</summary>
    public int Value { get; }

    /// <summary>Builds a minor unit from a digit count.</summary>
    /// <param name="value">The number of decimal digits.</param>
    /// <returns>The minor unit, or <see cref="Invalid"/>.</returns>
    public static ErrorOr<MinorUnit> Create(int value) =>
        value is >= 0 and <= MaxDigits ? new MinorUnit(value) : Invalid;

    /// <inheritdoc />
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
