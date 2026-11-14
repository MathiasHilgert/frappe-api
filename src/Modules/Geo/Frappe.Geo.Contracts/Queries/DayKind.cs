namespace Frappe.Geo.Contracts.Queries;

/// <summary>
/// The kind of a day's strongest observance: a holiday (paid as one when worked), a tourist non-working day, a
/// non-working day (both at the employer's choice) or a non-working day of one community only.
/// </summary>
public enum DayKind
{
    Holiday = 0,
    TouristNonWorkingDay = 1,
    NonWorkingDay = 2,
    CommunityNonWorkingDay = 3,
}
