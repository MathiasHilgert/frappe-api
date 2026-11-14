using Frappe.Aggregates;
using Frappe.Results;
using NodaTime;
using NodaTime.Text;

namespace Frappe.Geo.Domain.Holidays;

/// <summary>
/// The observances of one jurisdiction in one year, in two layers: what the source publishes (<see cref="Import(IReadOnlyCollection{Observance})"/>) and
/// what people decided by hand (<see cref="Override"/>, <see cref="Suppress"/>). The <see cref="Effective"/> calendar is
/// the source's, with each override winning over the observance of the same date and name. Its invariants: every
/// observance falls in the year, and no date holds two observances with the same name. Its existence is the coverage:
/// a calendar with no observances is a year without holidays, which is not the same as a year Geo has no data for.
/// </summary>
public sealed class HolidayCalendar : AggregateRoot
{
    private readonly List<CalendarEntry> _entries = [];

    private HolidayCalendar()
    {
        Jurisdiction = null!;
        Year = null!;
        Scope = null!;
    }

    private HolidayCalendar(Jurisdiction jurisdiction, HolidayYear year, CalendarScope scope)
    {
        Jurisdiction = jurisdiction;
        Year = year;
        Scope = scope;
    }

    public Jurisdiction Jurisdiction { get; private set; }

    public HolidayYear Year { get; private set; }

    /// <summary>Which of the jurisdiction's calendars of the year this is: the general one, or a sector's (<see cref="CalendarScope"/>).</summary>
    public CalendarScope Scope { get; private set; }

    public IReadOnlyCollection<CalendarEntry> Entries => _entries;

    /// <summary>The credit owed to the source of the imported layer, as it declared it on its last import.</summary>
    public Attribution? Attribution { get; private set; }

    /// <summary>
    /// How many times the entries changed. Every change moves it, so storing a change rewrites the calendar's own row
    /// and two writers that both read the same revision cannot both win.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>The source's observances, as last imported.</summary>
    public IReadOnlyList<Observance> Imported => Layer(CalendarLayer.Imported);

    /// <summary>The calendar that counts: the source's observances, overridden by hand where people decided so.</summary>
    public IReadOnlyList<Observance> Effective
    {
        get
        {
            var overridden = _entries.Where(e => e.Layer != CalendarLayer.Imported).Select(e => e.Key).ToHashSet();
            return [.. Imported.Where(o => !overridden.Contains(o.Key)).Concat(Layer(CalendarLayer.Added)).OrderBy(o => o.Date).ThenBy(o => o.Name, StringComparer.Ordinal)];
        }
    }

    /// <summary>The general calendar of a jurisdiction's year.</summary>
    public static HolidayCalendar Open(Jurisdiction jurisdiction, HolidayYear year) => Open(jurisdiction, year, CalendarScope.General);

    /// <summary>
    /// A new calendar of a jurisdiction's year and scope, announced as a change even before it holds an observance: its
    /// appearance turns "Geo has no data for that year" into an answer, so whatever was derived from the missing
    /// calendar (a cached "not covered") must be recomputed. One announcement per unit of work, whatever follows.
    /// </summary>
    public static HolidayCalendar Open(Jurisdiction jurisdiction, HolidayYear year, CalendarScope scope)
    {
        ArgumentNullException.ThrowIfNull(jurisdiction);
        ArgumentNullException.ThrowIfNull(year);
        ArgumentNullException.ThrowIfNull(scope);
        var calendar = new HolidayCalendar(jurisdiction, year, scope);
        calendar.Raise(new HolidayCalendarChanged(jurisdiction, year) { Scope = scope });
        return calendar;
    }

    /// <summary>The strongest kind among the date's effective observances; <see langword="null"/> for an ordinary day.</summary>
    public ObservanceKind? KindOf(LocalDate date) => Observance.StrongestKind(Effective.Where(o => o.Date == date));

    /// <summary>
    /// Replaces the source's layer with <paramref name="observances"/>, all or nothing. Overrides stay; when the source
    /// changed an observance an override decides, the override still wins and the conflict is raised.
    /// </summary>
    public Result<HolidayCalendar> Import(IReadOnlyCollection<Observance> observances)
    {
        ArgumentNullException.ThrowIfNull(observances);
        var errors = Validate(observances).ToArray();
        if (errors.Length > 0)
        {
            return Result.Failure<HolidayCalendar>(errors);
        }

        var before = Effective;
        if (Imported.ToHashSet().SetEquals(observances) && Imported.Count == observances.Count)
        {
            return this;
        }

        RaiseConflicts(Imported, observances);
        _entries.RemoveAll(e => e.Layer == CalendarLayer.Imported);
        _entries.AddRange(observances.Select(o => CalendarEntry.Of(CalendarLayer.Imported, o)));
        Revision++;
        RaiseIfChanged(before);
        return this;
    }

    /// <summary>
    /// Replaces the source's layer with <paramref name="observances"/> as <see cref="Import(IReadOnlyCollection{Observance})"/>
    /// does, and records the credit the source asks for, even when its observances did not change.
    /// </summary>
    public Result<HolidayCalendar> Import(IReadOnlyCollection<Observance> observances, Attribution attribution)
    {
        ArgumentNullException.ThrowIfNull(attribution);
        var imported = Import(observances);
        if (imported.IsSuccess)
        {
            Attribution = attribution;
        }

        return imported;
    }

    /// <summary>Adds an observance by hand, or replaces the source's of the same date and name.</summary>
    public Result<HolidayCalendar> Override(Observance observance)
    {
        ArgumentNullException.ThrowIfNull(observance);
        var errors = Validate([observance]).ToArray();
        if (errors.Length > 0)
        {
            return Result.Failure<HolidayCalendar>(errors);
        }

        var before = Effective;
        _entries.RemoveAll(e => e.Layer != CalendarLayer.Imported && e.Key == observance.Key);
        _entries.Add(CalendarEntry.Of(CalendarLayer.Added, observance));
        Revision++;
        RaiseIfChanged(before);
        return this;
    }

    /// <summary>
    /// Removes the source's observance of that date and name from the effective calendar, whatever the source says later.
    /// Only an observance the source publishes can be suppressed: one added by hand is removed with
    /// <see cref="RemoveOverride"/>, and a name the source does not have would be stored to no effect.
    /// </summary>
    public Result<HolidayCalendar> Suppress(LocalDate date, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (date.Year != Year.Value)
        {
            return OutsideYear(date);
        }

        if (!Imported.Any(o => o.Key == (date, name)))
        {
            return Error.RuleViolation(
                GeoErrorCodes.ObservanceNotInSource, ("jurisdiction", Jurisdiction.Value), ("date", LocalDatePattern.Iso.Format(date)), ("name", name));
        }

        var before = Effective;
        _entries.RemoveAll(e => e.Layer != CalendarLayer.Imported && e.Key == (date, name));
        _entries.Add(CalendarEntry.Suppression(date, name));
        Revision++;
        RaiseIfChanged(before);
        return this;
    }

    /// <summary>Drops the override of that date and name, so the source's observance (if any) counts again.</summary>
    public bool RemoveOverride(LocalDate date, string name)
    {
        var before = Effective;
        var removed = _entries.RemoveAll(e => e.Layer != CalendarLayer.Imported && e.Key == (date, name)) > 0;
        if (removed)
        {
            Revision++;
        }

        RaiseIfChanged(before);
        return removed;
    }

    private IEnumerable<Error> Validate(IReadOnlyCollection<Observance> observances) =>
        observances.Where(o => o.Date.Year != Year.Value).Select(o => OutsideYear(o.Date))
            .Concat(observances.GroupBy(o => o.Key).Where(g => g.Count() > 1)
                .Select(g => Error.RuleViolation(GeoErrorCodes.ObservanceDuplicate, ("date", LocalDatePattern.Iso.Format(g.Key.Date)), ("name", g.Key.Name))));

    private RuleViolationError OutsideYear(LocalDate date) =>
        Error.RuleViolation(GeoErrorCodes.ObservanceDateOutsideYear, ("date", LocalDatePattern.Iso.Format(date)), ("year", Year.Value));

    private void RaiseConflicts(IReadOnlyList<Observance> previous, IReadOnlyCollection<Observance> next)
    {
        var before = previous.ToDictionary(o => o.Key);
        var after = next.ToDictionary(o => o.Key);
        var changedUnderAnOverride = _entries
            .Where(e => e.Layer != CalendarLayer.Imported)
            .Select(e => e.Key)
            .Distinct()
            .Where(key => !Equals(before.GetValueOrDefault(key), after.GetValueOrDefault(key)));
        foreach (var key in changedUnderAnOverride)
        {
            Raise(new ObservanceOverrideConflicted(Jurisdiction, Year, key.Date, key.Name));
        }
    }

    private void RaiseIfChanged(IReadOnlyList<Observance> before)
    {
        // A change already announced and not yet handed over covers this one: readers recompute from the saved state.
        if (!before.SequenceEqual(Effective) && !DomainEvents.OfType<HolidayCalendarChanged>().Any())
        {
            Raise(new HolidayCalendarChanged(Jurisdiction, Year) { Scope = Scope });
        }
    }

    private Observance[] Layer(CalendarLayer layer) => [.. _entries.Where(e => e.Layer == layer).Select(e => e.ToObservance())];
}
