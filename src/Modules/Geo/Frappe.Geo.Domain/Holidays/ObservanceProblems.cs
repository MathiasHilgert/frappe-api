using Frappe.Results;
using NodaTime;
using NodaTime.Text;

namespace Frappe.Geo.Domain.Holidays;

/// <summary>
/// Why an observance is not valid: the stable <c>reason</c> of a <see cref="GeoErrorCodes.ObservanceInvalid"/> error,
/// never English text, so each language translates it. Every error names the observance's date in ISO form
/// (<c>yyyy-MM-dd</c>), or the value given when that is not a date.
/// </summary>
public static class ObservanceProblems
{
    public const string DateInvalid = "date_invalid";
    public const string NameEmpty = "name_empty";
    public const string NameTooLong = "name_too_long";
    public const string KindUnknown = "kind_unknown";
    public const string MovedFromInvalid = "moved_from_invalid";
    public const string MovedFromNotAllowed = "moved_from_not_allowed";
    public const string CommunityMissing = "community_missing";
    public const string CommunityNotAllowed = "community_not_allowed";
    public const string CommunityTooLong = "community_too_long";
    public const string CommunityUnknown = "community_unknown";
    public const string CitationEmpty = "citation_empty";
    public const string CitationTooLong = "citation_too_long";
    public const string UrlInvalid = "url_invalid";
    public const string UrlTooLong = "url_too_long";

    /// <summary>An observance on <paramref name="date"/> is not valid for <paramref name="reason"/>, with the facts that explain it.</summary>
    public static InvalidValueError Invalid(LocalDate date, string reason, params (string Name, object? Value)[] facts) =>
        Invalid(LocalDatePattern.Iso.Format(date), reason, facts);

    /// <summary>The same, when the date itself is what the client wrote (it may not be a date).</summary>
    public static InvalidValueError Invalid(string? date, string reason, params (string Name, object? Value)[] facts) =>
        Error.InvalidValue(GeoErrorCodes.ObservanceInvalid, [("date", date ?? string.Empty), ("reason", reason), .. facts]);
}
