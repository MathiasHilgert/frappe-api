using Frappe.Geo.Domain.Cities;

namespace Frappe.Geo.Application.Cities;

/// <summary>
/// The terms of a city search: the folded query, for equality, prefix and similarity, and its words, each also escaped
/// for a <c>LIKE</c> pattern (backslash first, then <c>%</c> and <c>_</c>), so a typed wildcard matches literally.
/// </summary>
public sealed record CitySearchTerms
{
    /// <summary>The fewest characters a folded query has.</summary>
    public const int MinLength = 2;

    /// <summary>The most characters a folded query has.</summary>
    public const int MaxLength = 100;

    /// <summary>The query length from which similar spellings match too.</summary>
    public const int FuzzyLength = 3;

    /// <summary>The most words a query is split into; later words are ignored.</summary>
    public const int MaxTokens = 5;

    private static readonly char[] _separators = [' ', ','];

    private CitySearchTerms(string plain, IReadOnlyList<CitySearchToken> tokens)
    {
        Plain = plain;
        Escaped = Escape(plain);
        Tokens = tokens;
    }

    /// <summary>Gets the folded query's words joined by single spaces, commas dropped, such as <c>pilar cordoba</c>.</summary>
    public string Plain { get; }

    /// <summary>Gets the folded query escaped for a <c>LIKE</c> pattern.</summary>
    public string Escaped { get; }

    /// <summary>Gets the words of the query, one to <see cref="MaxTokens"/>.</summary>
    public IReadOnlyList<CitySearchToken> Tokens { get; }

    /// <summary>Gets whether the query is long enough to match similar spellings.</summary>
    public bool Fuzzy => Plain.Length >= FuzzyLength;

    /// <summary>Builds the terms of a query typed by a client.</summary>
    /// <param name="q">The raw query.</param>
    /// <returns>The terms, or <c>geo.city.query_invalid</c> when the folded query is not 2 to 100 characters or has no word.</returns>
    public static ErrorOr<CitySearchTerms> Create(string? q)
    {
        // Commas only separate words, so a label typed back ("Pilar, Córdoba") reads as its words.
        string[] words = SearchText
            .Fold(q ?? string.Empty)
            .Split(_separators, StringSplitOptions.RemoveEmptyEntries);
        string plain = string.Join(' ', words);
        CitySearchToken[] tokens =
        [
            .. words
                .Take(MaxTokens)
                .Select(static token => new CitySearchToken(token, Escape(token))),
        ];
        return plain.Length is < MinLength or > MaxLength || tokens.Length == 0
            ? CityErrors.QueryInvalid
            : new CitySearchTerms(plain, tokens);
    }

    private static string Escape(string text) =>
        text.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
