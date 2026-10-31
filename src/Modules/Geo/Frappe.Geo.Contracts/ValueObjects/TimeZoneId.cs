using System.Collections.Frozen;
using NodaTime;

namespace Frappe.Geo.Contracts.ValueObjects;

/// <summary>An IANA time zone id known to the bundled tzdb, with its tzdb casing, such as <c>America/Argentina/Cordoba</c>.</summary>
/// <remarks>The id is kept as chosen: a link such as <c>Europe/Oslo</c> is never replaced by its target, which belongs to another country.</remarks>
public sealed record TimeZoneId
{
    // Every tzdb id, links included, keyed case-insensitively to its own tzdb casing.
    private static readonly FrozenDictionary<string, string> _ids =
        DateTimeZoneProviders.Tzdb.Ids.ToFrozenDictionary(
            static id => id,
            StringComparer.OrdinalIgnoreCase
        );

    private TimeZoneId(string value) => Value = value;

    /// <summary>Gets the error returned for a value that is not a tzdb id.</summary>
    public static Error Invalid { get; } = Error.Validation("geo.time_zone_id.invalid");

    /// <summary>Gets the tzdb id.</summary>
    public string Value { get; }

    /// <summary>Builds a time zone id from untrusted input, trimmed and matched case-insensitively.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The id with its tzdb casing, or <see cref="Invalid"/>.</returns>
    public static ErrorOr<TimeZoneId> Create(string? value) =>
        _ids.TryGetValue(value?.Trim() ?? string.Empty, out string? id)
            ? new TimeZoneId(id)
            : Invalid;

    /// <inheritdoc />
    public override string ToString() => Value;
}
