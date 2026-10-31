using System.Text.RegularExpressions;
using Frappe.Geo.Contracts.ValueObjects;

namespace Frappe.Geo.Domain.Addresses;

/// <summary>A country's postal-code rule: a pattern the whole code must match, and whether a code is required.</summary>
public sealed class PostalCodeFormat
{
    private static readonly TimeSpan _matchTimeout = TimeSpan.FromMilliseconds(100);

    private readonly Regex? _pattern;
    private readonly bool _required;

    private PostalCodeFormat(Regex? pattern, bool required)
    {
        _pattern = pattern;
        _required = required;
    }

    /// <summary>Gets how long one match may run before the code is rejected.</summary>
    public TimeSpan MatchTimeout => _pattern?.MatchTimeout ?? _matchTimeout;

    /// <summary>Builds the rule from seeded data.</summary>
    /// <param name="pattern">The pattern, anchored here at both ends; <see langword="null"/> accepts any code.</param>
    /// <param name="required">Whether an address must carry a postal code.</param>
    /// <returns>The rule.</returns>
    public static PostalCodeFormat Create(string? pattern, bool required) =>
        new(
            pattern is null
                ? null
                : new Regex(
                    $@"\A(?:{pattern})\z",
                    RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
                    _matchTimeout
                ),
            required
        );

    /// <summary>Checks a postal code against the rule.</summary>
    /// <param name="postalCode">The code, or <see langword="null"/> when the address carries none.</param>
    /// <returns>Success, <c>geo.postal_code.invalid</c> or <c>geo.postal_code.required</c>.</returns>
    public ErrorOr<Success> Validate(PostalCode? postalCode)
    {
        if (postalCode is null)
        {
            return _required ? AddressErrors.PostalCodeRequired : Result.Success;
        }

        try
        {
            return _pattern is null || _pattern.IsMatch(postalCode.Value)
                ? Result.Success
                : AddressErrors.PostalCodeInvalid;
        }
        catch (RegexMatchTimeoutException)
        {
            // A pattern that cannot decide in time proves nothing about the code, so it is not accepted.
            return AddressErrors.PostalCodeInvalid;
        }
    }
}
