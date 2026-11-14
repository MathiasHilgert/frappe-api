using Frappe.Geo.Domain.Holidays;
using NodaTime;

namespace Frappe.Geo.Domain.BusinessDays;

/// <summary>
/// One date under one policy: whether it is a business day, whether it falls on the weekend, its kind (the strongest
/// of its observances; <see langword="null"/> for an ordinary day) and the observances the policy looked at.
/// </summary>
public sealed record BusinessDay(LocalDate Date, bool IsBusinessDay, bool IsWeekend, ObservanceKind? Kind, IReadOnlyList<Observance> Observances);
