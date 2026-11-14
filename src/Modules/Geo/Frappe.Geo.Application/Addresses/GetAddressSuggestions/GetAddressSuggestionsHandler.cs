using Frappe.Geo.Application.Countries;
using Frappe.Geo.Domain;
using Frappe.Geo.Domain.Countries;
using Frappe.Localization;
using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Geo.Application.Addresses.GetAddressSuggestions;

/// <summary>
/// Input is validated first, the country against the countries Geo knows (so <c>XX</c> is no filter): a malformed request
/// is the client's error whether or not a provider is configured, and only a valid one reaches the provider. A session's
/// first answered suggestion opens it and counts it as started (S15).
/// </summary>
internal sealed class GetAddressSuggestionsHandler(IAddressProvider provider, ICountryCatalog countries, IAddressSessions sessions, GeoMetrics metrics)
    : IQueryHandler<GetAddressSuggestions, IReadOnlyList<AddressSuggestion>>
{
    /// <summary>The longest text searched: an address, not a document.</summary>
    public const int MaxQueryLength = 200;

    public async Task<Result<IReadOnlyList<AddressSuggestion>>> HandleAsync(GetAddressSuggestions query, CancellationToken cancellationToken)
    {
        var search = await SearchOfAsync(query, cancellationToken);
        if (!search.IsSuccess)
        {
            return Result.Failure<IReadOnlyList<AddressSuggestion>>(search.Errors);
        }

        var found = await new AddressProviderCalls(provider, metrics).RunAsync(
            AddressOperation.Suggest,
            async p => (IReadOnlyList<ProviderSuggestion>?)await p.SuggestAsync(search.Value, cancellationToken),
            GeoErrors.AddressNotFound);
        if (!found.IsSuccess)
        {
            return Result.Failure<IReadOnlyList<AddressSuggestion>>(found.Errors);
        }

        if (await sessions.OpenAsync(provider.Name, search.Value.Session, provider.SessionLifetime, cancellationToken))
        {
            metrics.AddressSessionStarted(provider.Name);
        }

        return Result.Success<IReadOnlyList<AddressSuggestion>>([.. found.Value.Select(ToSuggestion)]);
    }

    /// <summary>Every input is checked at once, so a client learns all its mistakes in one answer.</summary>
    private async Task<Result<AddressSearch>> SearchOfAsync(GetAddressSuggestions query, CancellationToken cancellationToken)
    {
        var text = TextOf(query.Query);
        var session = AddressSession.Create(query.Session);
        var country = query.Country is null ? null : await KnownCountryAsync(query.Country, cancellationToken);
        var language = query.Language is null ? query.RequestLanguage : LanguageTag.Create(query.Language);
        Error[] errors = [.. text.Errors, .. session.Errors, .. country?.Errors ?? [], .. language.Errors];
        return errors.Length > 0
            ? Result.Failure<AddressSearch>(errors)
            : new AddressSearch(text.Value, session.Value, country?.Value, language.Value);
    }

    /// <summary>The trimmed text searched: required, and at most <see cref="MaxQueryLength"/> characters.</summary>
    private static Result<string> TextOf(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return GeoErrors.ParameterRequired("q");
        }

        var text = raw.Trim();
        return text.Length > MaxQueryLength ? GeoErrors.AddressQueryInvalid(MaxQueryLength) : text;
    }

    /// <summary>An ISO 3166-1 alpha-2 code of a country Geo knows; a well-formed code that names none (<c>XX</c>) is not one either.</summary>
    private async Task<Result<CountryCode>> KnownCountryAsync(string raw, CancellationToken cancellationToken)
    {
        var code = CountryCode.Create(raw);
        return !code.IsSuccess || await countries.FindAsync(code.Value, cancellationToken) is not null
            ? code
            : Error.InvalidValue(GeoErrorCodes.CountryCodeInvalidFormat, ("value", raw));
    }

    private AddressSuggestion ToSuggestion(ProviderSuggestion suggestion) =>
        new(new AddressReference(provider.Name, suggestion.ProviderId).Encode(), suggestion.Text, suggestion.MainText, suggestion.SecondaryText);
}
