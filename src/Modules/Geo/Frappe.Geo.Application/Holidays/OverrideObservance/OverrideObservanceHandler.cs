using Frappe.Geo.Application.BusinessDays;
using Frappe.Geo.Application.Countries;
using Frappe.Geo.Application.Territory;
using Frappe.Geo.Domain.Holidays;
using Frappe.Results;
using Frappe.UseCases;
using NodaTime;
using NodaTime.Text;

namespace Frappe.Geo.Application.Holidays.OverrideObservance;

/// <summary>
/// Reads every field first and answers all their problems at once (400); only a readable observance looks for its
/// calendar (404 when Geo has none) and checks its community against the ones the country declares.
/// </summary>
internal sealed class OverrideObservanceHandler(IHolidayCalendars calendars, ICountryCatalog countries, ITerritoryDirectory territory, ICalendarRulesCatalog rules)
    : ICommandHandler<OverrideObservance, Unit>
{
    public async Task<Result<Unit>> HandleAsync(OverrideObservance command, CancellationToken cancellationToken)
    {
        var observance = Read(command);
        if (!observance.IsSuccess)
        {
            return Result.Failure<Unit>(observance.Errors);
        }

        var target = await new CalendarToEdit(calendars, countries, territory, rules).FindAsync(command.Jurisdiction, observance.Value.Date, command.Scope, cancellationToken);
        if (!target.IsSuccess)
        {
            return Result.Failure<Unit>(target.Errors);
        }

        var (calendar, declared) = target.Value;
        var communities = declared?.Communities ?? new HashSet<string>();
        if (observance.Value.Kind == ObservanceKind.CommunityNonWorkingDay && observance.Value.Community is { } community && !communities.Contains(community))
        {
            return ObservanceProblems.Invalid(
                observance.Value.Date, ObservanceProblems.CommunityUnknown, ("value", community), ("communities", communities.Order(StringComparer.Ordinal).ToArray()));
        }

        var changed = calendar.Override(observance.Value);
        return changed.IsSuccess ? Unit.Value : Result.Failure<Unit>(changed.Errors);
    }

    private static Result<Observance> Read(OverrideObservance command)
    {
        var notification = new Notification();
        var date = notification.Collect(ObservanceInput.Date(command.Date));
        var at = new Where(command.Date, notification.HasErrors ? null : date);
        var name = notification.Collect(GeoErrors.Required(command.Name, "name"));
        var kind = Kind(command.Kind, at, notification);
        var movedFrom = MovedFrom(command.MovedFrom, at, notification);
        var basis = Basis(command, at, notification);

        // The domain adds what it checks itself (name, consistency, community and basis bounds), so all come at once,
        // leaving out what only restates a problem already reported: an absent name, or a kind that could not be read.
        // Without a readable date it still checks what does not depend on one, on a stand-in date, and each problem
        // names the date the client wrote; whether a holiday may move from another year needs the real date.
        var observance = Observance.Create(at.Date ?? StandInDate, name, kind ?? ObservanceKind.Holiday, movedFrom, command.Community?.Trim(), basis);
        var restated = Restated(name is null, kind is null, at.Date is null);
        foreach (var error in observance.Errors.Where(e => !restated.Contains(ReasonOf(e))))
        {
            notification.Add(at.Date is null ? at.Invalid(ReasonOf(error), FactsOf(error)) : error);
        }

        return notification.HasErrors ? notification.ToFailure<Observance>() : observance;
    }

    /// <summary>The domain's problems that only restate one already reported, or that need what could not be read.</summary>
    private static HashSet<string> Restated(bool noName, bool noKind, bool noDate)
    {
        HashSet<string> restated = noName ? [ObservanceProblems.NameEmpty] : [];
        if (noKind)
        {
            restated.UnionWith([ObservanceProblems.MovedFromNotAllowed, ObservanceProblems.CommunityMissing, ObservanceProblems.CommunityNotAllowed]);
        }

        if (noDate)
        {
            restated.Add(ObservanceProblems.MovedFromNotAllowed);
        }

        return restated;
    }

    /// <summary>The date the domain's checks run on when the client's cannot be read; it never reaches a calendar.</summary>
    private static readonly LocalDate StandInDate = new(2000, 1, 1);

    private static string ReasonOf(Error error) => error.Parameters["reason"] as string ?? string.Empty;

    /// <summary>What explains a domain problem, without the date and reason every one of them carries.</summary>
    private static (string Name, object? Value)[] FactsOf(Error error) =>
        [.. error.Parameters.Where(p => p.Key is not ("date" or "reason")).Select(p => (p.Key, p.Value))];

    private static ObservanceKind? Kind(string? raw, Where at, Notification notification)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            notification.Add(GeoErrors.ParameterRequired("kind"));
            return null;
        }

        if (ObservanceKinds.TryParse(raw, out var kind))
        {
            return kind;
        }

        notification.Add(at.Invalid(ObservanceProblems.KindUnknown, ("value", raw)));
        return null;
    }

    private static LocalDate? MovedFrom(string? raw, Where at, Notification notification)
    {
        if (raw is null)
        {
            return null;
        }

        if (LocalDatePattern.Iso.Parse(raw) is { Success: true } parsed)
        {
            return parsed.Value;
        }

        notification.Add(at.Invalid(ObservanceProblems.MovedFromInvalid, ("value", raw)));
        return null;
    }

    /// <summary>A basis is given when either field is; an address that is not even a URI reference is invalid here, the rest in the domain.</summary>
    private static LegalBasis? Basis(OverrideObservance command, Where at, Notification notification)
    {
        if (command.LegalCitation is null && command.LegalUrl is null)
        {
            return null;
        }

        Uri? url = null;
        if (command.LegalUrl is not null && !Uri.TryCreate(command.LegalUrl.Trim(), UriKind.RelativeOrAbsolute, out url))
        {
            notification.Add(at.Invalid(ObservanceProblems.UrlInvalid));
        }

        return new LegalBasis(command.LegalCitation?.Trim() ?? string.Empty, url);
    }

    /// <summary>The observance's date for its errors: the ISO date when readable, else what the client wrote.</summary>
    private sealed record Where(string? Raw, LocalDate? Date)
    {
        public InvalidValueError Invalid(string reason, params (string Name, object? Value)[] facts) =>
            Date is { } date ? ObservanceProblems.Invalid(date, reason, facts) : ObservanceProblems.Invalid(Raw, reason, facts);
    }
}
