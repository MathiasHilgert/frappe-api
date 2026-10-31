using System.Collections.Frozen;
using NodaTime.TimeZones;

namespace Frappe.Kernel.Geography;

/// <summary>An IANA time zone id known to the bundled tzdb, in its canonical form, such as <c>America/Argentina/Cordoba</c>.</summary>
public sealed record TimeZoneId
{
    // Every tzdb id, aliases included, to its canonical id, so a rename such as Europe/Kiev is stored as Europe/Kyiv.
    private static readonly FrozenDictionary<string, string> _canonicalIds =
        TzdbDateTimeZoneSource.Default.CanonicalIdMap.ToFrozenDictionary(
            StringComparer.OrdinalIgnoreCase
        );

    private TimeZoneId(string value) => Value = value;

    /// <summary>Gets the error returned for a value that is not a tzdb id.</summary>
    public static Error Invalid { get; } =
        Error.Validation(
            "kernel.time_zone_id.invalid",
            "The time zone must be an IANA time zone id such as America/Argentina/Buenos_Aires."
        );

    /// <summary>Gets the canonical tzdb id.</summary>
    public string Value { get; }

    /// <summary>Builds a time zone id from untrusted input, trimmed and matched case-insensitively.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The canonical id, or <see cref="Invalid"/>.</returns>
    public static ErrorOr<TimeZoneId> Create(string? value) =>
        _canonicalIds.TryGetValue(value?.Trim() ?? string.Empty, out string? canonical)
            ? new TimeZoneId(canonical)
            : Invalid;

    /// <inheritdoc />
    public override string ToString() => Value;
}
