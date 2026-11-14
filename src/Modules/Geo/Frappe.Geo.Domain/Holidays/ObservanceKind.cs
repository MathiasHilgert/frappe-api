namespace Frappe.Geo.Domain.Holidays;

/// <summary>
/// What a date is, from the strongest to the weakest (the order is the declaration order): a day's kind is that of its
/// strongest observance. The kinds follow the law, not one country's words: a holiday is paid as one when worked; a
/// tourist non-working day closes banks but leaves work to the employer; a non-working day applies to everyone; a
/// community non-working day only to the members of that community.
/// </summary>
public enum ObservanceKind
{
    Holiday = 0,
    TouristNonWorkingDay = 1,
    NonWorkingDay = 2,
    CommunityNonWorkingDay = 3,
}
