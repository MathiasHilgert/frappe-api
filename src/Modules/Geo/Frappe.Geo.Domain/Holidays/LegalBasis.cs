namespace Frappe.Geo.Domain.Holidays;

/// <summary>
/// The norm an observance comes from, as cited (<c>Ley 27.399, art. 1</c>), with a link to its text when known: an
/// absolute <c>http</c> or <c>https</c> address. <see cref="Observance.Create"/> checks it.
/// </summary>
public sealed record LegalBasis(string Citation, Uri? Text)
{
    public const int MaxCitationLength = 200;
    public const int MaxTextLength = 500;

    /// <summary>What is wrong with it, as reasons of <see cref="ObservanceProblems"/> and their facts; empty when valid.</summary>
    internal IEnumerable<(string Reason, (string Name, object? Value)[] Facts)> Problems()
    {
        var citation = Citation?.Trim() ?? string.Empty;
        if (citation.Length == 0)
        {
            yield return (ObservanceProblems.CitationEmpty, []);
        }
        else if (citation.Length > MaxCitationLength)
        {
            yield return (ObservanceProblems.CitationTooLong, [("max", MaxCitationLength)]);
        }

        if (Text is null)
        {
            yield break;
        }

        if (!Text.IsAbsoluteUri || (Text.Scheme != Uri.UriSchemeHttps && Text.Scheme != Uri.UriSchemeHttp))
        {
            yield return (ObservanceProblems.UrlInvalid, []);
        }
        else if (Text.AbsoluteUri.Length > MaxTextLength)
        {
            yield return (ObservanceProblems.UrlTooLong, [("max", MaxTextLength)]);
        }
    }
}
