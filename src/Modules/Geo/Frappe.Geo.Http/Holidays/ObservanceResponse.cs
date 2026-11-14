using Frappe.Geo.Application.Holidays;
using Frappe.Geo.Domain.Holidays;
using NodaTime.Text;

namespace Frappe.Geo.Http.Holidays;

/// <summary>
/// One reason a date is not an ordinary day. <c>kind</c> is <c>holiday</c>, <c>tourist_non_working_day</c>,
/// <c>non_working_day</c> or <c>community_non_working_day</c> (with its <c>community</c>); <c>movedFrom</c> is the
/// original date of a moved holiday; <c>legalBasis</c> cites its norm and links its text (<c>url</c>) when known.
/// </summary>
public sealed record ObservanceResponse(string Date, string Name, string Kind, string? MovedFrom, string? Community, LegalBasisResponse? LegalBasis)
{
    internal static ObservanceResponse Of(Observance o) => new(
        LocalDatePattern.Iso.Format(o.Date),
        o.Name,
        ObservanceKinds.NameOf(o.Kind),
        o.MovedFrom is { } from ? LocalDatePattern.Iso.Format(from) : null,
        o.Community,
        o.LegalBasis is { } basis ? new LegalBasisResponse(basis.Citation, basis.Text) : null);
}
