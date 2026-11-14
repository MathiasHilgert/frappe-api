using Frappe.Results;
using NodaTime;

namespace Frappe.Geo.Domain.Holidays;

/// <summary>
/// One reason a date is not an ordinary day: its name, its kind, the date it was moved from (a movable holiday), the
/// community it applies to (a community non-working day) and its legal basis. Identified within a calendar by its date
/// and name, since one date may hold several observances.
/// </summary>
public sealed record Observance
{
    public const int MaxNameLength = 200;

    /// <summary>A community is a short code (<c>jewish</c>); which ones exist is each country's data.</summary>
    public const int MaxCommunityLength = 32;

    private Observance(LocalDate date, string name, ObservanceKind kind, LocalDate? movedFrom, string? community, LegalBasis? legalBasis)
    {
        Date = date;
        Name = name;
        Kind = kind;
        MovedFrom = movedFrom;
        Community = community;
        LegalBasis = legalBasis;
    }

    public LocalDate Date { get; }

    public string Name { get; }

    public ObservanceKind Kind { get; }

    /// <summary>The date a movable holiday was moved from; <see langword="null"/> when it stayed on its date or cannot move.</summary>
    public LocalDate? MovedFrom { get; }

    /// <summary>Who a community non-working day applies to (<c>jewish</c>); <see langword="null"/> for every other kind.</summary>
    public string? Community { get; }

    public LegalBasis? LegalBasis { get; }

    public static Result<Observance> Create(LocalDate date, string? name, ObservanceKind kind, LocalDate? movedFrom = null, string? community = null, LegalBasis? legalBasis = null)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        Error[] problems = [.. Problems(date, trimmed, kind, movedFrom, community, legalBasis)];
        return problems.Length == 0
            ? new Observance(date, trimmed, kind, StayedPut(date, movedFrom), community, legalBasis)
            : Result.Failure<Observance>(problems);
    }

    /// <summary>A holiday "moved" to its own date did not move.</summary>
    private static LocalDate? StayedPut(LocalDate date, LocalDate? movedFrom) => movedFrom == date ? null : movedFrom;

    /// <summary>Every problem at once, so a client fixes them in one go.</summary>
    private static IEnumerable<InvalidValueError> Problems(LocalDate date, string name, ObservanceKind kind, LocalDate? movedFrom, string? community, LegalBasis? legalBasis) =>
        NameProblem(name)
            .Concat(movedFrom is { } from && (kind != ObservanceKind.Holiday || from.Year != date.Year) ? [(ObservanceProblems.MovedFromNotAllowed, [])] : [])
            .Concat(CommunityProblem(kind, community))
            .Concat(legalBasis?.Problems() ?? [])
            .Select(p => ObservanceProblems.Invalid(date, p.Reason, p.Facts));

    private static IEnumerable<(string Reason, (string Name, object? Value)[] Facts)> NameProblem(string name) => name.Length switch
    {
        0 => [(ObservanceProblems.NameEmpty, [])],
        > MaxNameLength => [(ObservanceProblems.NameTooLong, [("max", MaxNameLength)])],
        _ => [],
    };

    /// <summary>A community exactly for a community non-working day, and a short one.</summary>
    private static IEnumerable<(string Reason, (string Name, object? Value)[] Facts)> CommunityProblem(ObservanceKind kind, string? community)
    {
        var isCommunityDay = kind == ObservanceKind.CommunityNonWorkingDay;
        var given = !string.IsNullOrWhiteSpace(community);
        if (isCommunityDay != given)
        {
            return [(isCommunityDay ? ObservanceProblems.CommunityMissing : ObservanceProblems.CommunityNotAllowed, [])];
        }

        return community is { Length: > MaxCommunityLength } ? [(ObservanceProblems.CommunityTooLong, [("max", MaxCommunityLength)])] : [];
    }

    /// <summary>The kind of a day holding <paramref name="observances"/>: the strongest of theirs; <see langword="null"/> for none.</summary>
    public static ObservanceKind? StrongestKind(IEnumerable<Observance> observances) =>
        observances.Select(o => (ObservanceKind?)o.Kind).Min();

    /// <summary>The identity of an observance within its calendar.</summary>
    public (LocalDate Date, string Name) Key => (Date, Name);
}
