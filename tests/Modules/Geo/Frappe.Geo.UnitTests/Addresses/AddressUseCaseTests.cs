using Frappe.Geo.Application;
using Frappe.Geo.Application.Addresses;
using Frappe.Geo.Application.Addresses.GetAddressSuggestions;
using Frappe.Geo.Application.Addresses.ResolveAddress;
using Frappe.Geo.Domain;
using Frappe.Localization;
using Frappe.Observability;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Geo.UnitTests.Addresses;

/// <summary>
/// Suggesting and resolving addresses (S11) through the registered use cases, with a fake provider: opaque ids,
/// required sessions, normalization to ISO codes, and "unavailable" when the provider is.
/// </summary>
public sealed class AddressUseCaseTests
{
    private const string Session = "5b1c1a5e-3f5d-4e0b-9f53-4d1c2b9e8a10";
    private static readonly LanguageTag English = LanguageTag.Create("en").Value;

    private sealed record Harness(ServiceProvider Services, IAddressProvider AddressProvider) : IAsyncDisposable
    {
        public Task<Result<IReadOnlyList<AddressSuggestion>>> SuggestAsync(string? q, string? session = Session, string? country = null, string? language = null) =>
            Services.GetRequiredService<IQueryHandler<GetAddressSuggestions, IReadOnlyList<AddressSuggestion>>>()
                .HandleAsync(new GetAddressSuggestions(q, session, country, language, English), CancellationToken.None);

        public Task<Result<AddressSnapshot>> ResolveAsync(string reference, string? session = Session, string? language = null) =>
            Services.GetRequiredService<IQueryHandler<ResolveAddress, AddressSnapshot>>()
                .HandleAsync(new ResolveAddress(reference, session, language, English), CancellationToken.None);

        public FakeAddressProvider Provider => (FakeAddressProvider)AddressProvider;

        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }

    private static Harness Create(ISubdivisionNames? names = null, IAddressProvider? provider = null)
    {
        provider ??= new FakeAddressProvider();
        var services = new ServiceCollection()
            .AddLogging()
            .AddMetrics()
            .AddSingleton(provider)
            .AddSingleton(names ?? new FakeSubdivisionNames(("AR", "CABA", "AR-C")))
            .AddSingleton<Frappe.Geo.Application.Countries.ICountryCatalog>(new FakeCountryCatalog("AR", "UY"))
            .AddHandlers(typeof(GeoMetrics).Assembly)
            .BuildServiceProvider();
        return new Harness(services, provider);
    }

    private static string Reference(string id = FakeAddressProvider.Known) => new AddressReference("fake", id).Encode();

    [Test]
    public async Task Suggestions_carry_opaque_ids_that_resolve_to_the_suggested_place()
    {
        await using var harness = Create();

        var suggestions = await harness.SuggestAsync("corrientes 1234", country: "ar", language: "es");

        var suggestion = suggestions.Value.Single();
        await Assert.That(suggestion.Id).IsEqualTo(Reference());
        await Assert.That(suggestion.Id).DoesNotContain(FakeAddressProvider.Known);
        var search = harness.Provider.Searches.Single();
        await Assert.That(search.Country!.Value).IsEqualTo("AR");
        await Assert.That(search.Language.Value).IsEqualTo("es");
        await Assert.That(search.Session.Value).IsEqualTo(Session);
        await Assert.That((await harness.ResolveAsync(suggestion.Id)).IsSuccess).IsTrue();
    }

    [Test]
    public async Task Without_a_language_the_request_language_is_used()
    {
        await using var harness = Create();

        await harness.SuggestAsync("corrientes");

        await Assert.That(harness.Provider.Searches.Single().Language).IsEqualTo(English);
    }

    [Test]
    [Arguments(null, GeoErrorCodes.ParameterRequired)]
    [Arguments("", GeoErrorCodes.ParameterRequired)]
    [Arguments("not-a-uuid", GeoErrorCodes.AddressSessionInvalid)]
    public async Task A_session_is_required_to_suggest_and_to_resolve(string? session, string code)
    {
        await using var harness = Create();

        var suggestions = await harness.SuggestAsync("corrientes", session);
        var resolved = await harness.ResolveAsync(Reference(), session);

        await Assert.That(suggestions.Errors.Select(e => e.Code)).IsEquivalentTo([code]);
        await Assert.That(resolved.Errors.Select(e => e.Code)).IsEquivalentTo([code]);
        await Assert.That(harness.Provider.Searches).IsEmpty();
        await Assert.That(harness.Provider.Resolutions).IsEmpty();
    }

    [Test]
    [Arguments(null)]
    [Arguments("   ")]
    public async Task A_missing_query_is_required_and_a_too_long_one_invalid(string? q)
    {
        await using var harness = Create();

        var empty = await harness.SuggestAsync(q);
        var tooLong = await harness.SuggestAsync(new string('a', 201));

        await Assert.That(empty.Errors.Single()).IsTypeOf<InvalidValueError>();
        await Assert.That(empty.Errors.Single().Code).IsEqualTo(GeoErrorCodes.ParameterRequired);
        await Assert.That(empty.Errors.Single().Parameters["parameter"]).IsEqualTo("q");
        await Assert.That(tooLong.Errors.Single().Code).IsEqualTo(GeoErrorCodes.AddressQueryInvalid);
    }

    [Test]
    public async Task A_bad_country_or_language_filter_is_invalid()
    {
        await using var harness = Create();

        var result = await harness.SuggestAsync("corrientes", country: "ARG", language: "not a tag");

        await Assert.That(result.Errors.Count).IsEqualTo(2);
        await Assert.That(result.Errors.All(e => e is InvalidValueError)).IsTrue();
    }

    [Test]
    [Arguments("not base64url!", GeoErrorCodes.AddressReferenceInvalid)]
    [Arguments("ChIJ3S-JXmauEmsRUcIaWtf4MzE", GeoErrorCodes.AddressReferenceInvalid)]
    [Arguments("", GeoErrorCodes.ParameterRequired)]
    [Arguments("  ", GeoErrorCodes.ParameterRequired)]
    public async Task A_missing_malformed_or_forged_reference_is_refused_and_never_reaches_the_provider(string reference, string code)
    {
        await using var harness = Create();

        var result = await harness.ResolveAsync(reference);

        await Assert.That(result.Errors.Single()).IsTypeOf<InvalidValueError>();
        await Assert.That(result.Errors.Single().Code).IsEqualTo(code);
        await Assert.That(harness.Provider.Resolutions).IsEmpty();
    }

    [Test]
    public async Task A_well_formed_reference_the_provider_does_not_know_is_not_found()
    {
        await using var harness = Create();

        var unknown = await harness.ResolveAsync(Reference("nowhere"));
        var foreign = await harness.ResolveAsync(new AddressReference("other", FakeAddressProvider.Known).Encode());

        await Assert.That(unknown.Errors.Single()).IsTypeOf<NotFoundError>();
        await Assert.That(unknown.Errors.Single().Code).IsEqualTo(GeoErrorCodes.AddressNotFound);
        await Assert.That(foreign.Errors.Single().Code).IsEqualTo(GeoErrorCodes.AddressNotFound);
    }

    [Test]
    public async Task The_resolved_address_is_the_snapshot_with_ISO_codes_and_the_province_mapped_through_its_alias()
    {
        await using var harness = Create();

        var resolved = await harness.ResolveAsync(Reference());

        await Assert.That(resolved.Value).IsEqualTo(new AddressSnapshot(
            Reference(), "Av. Corrientes 1234, Buenos Aires", "Avenida Corrientes", "1234", "C1043", "Buenos Aires", "AR", "AR-C",
            new Frappe.Geo.Application.Territory.Coordinates(-34.6, -58.38)));
    }

    [Test]
    public async Task A_province_Geo_cannot_map_answers_no_subdivision_code()
    {
        await using var harness = Create(new FakeSubdivisionNames());

        var resolved = await harness.ResolveAsync(Reference());

        await Assert.That(resolved.Value.CountryCode).IsEqualTo("AR");
        await Assert.That(resolved.Value.SubdivisionCode).IsNull();
    }

    [Test]
    public async Task A_country_that_is_not_an_ISO_code_answers_none()
    {
        await using var harness = Create();
        harness.Provider.Places[FakeAddressProvider.Known] = harness.Provider.Places[FakeAddressProvider.Known] with { CountryCode = null };

        var resolved = await harness.ResolveAsync(Reference());

        await Assert.That(resolved.Value.CountryCode).IsNull();
        await Assert.That(resolved.Value.SubdivisionCode).IsNull();
    }

    [Test]
    public async Task When_the_provider_is_down_both_answer_unavailable()
    {
        await using var harness = Create();
        harness.Provider.Down = true;

        var suggestions = await harness.SuggestAsync("corrientes");
        var resolved = await harness.ResolveAsync(Reference());

        await Assert.That(suggestions.Errors.Single()).IsTypeOf<UnavailableError>();
        await Assert.That(suggestions.Errors.Single().Code).IsEqualTo(GeoErrorCodes.AddressesUnavailable);
        await Assert.That(resolved.Errors.Single().Code).IsEqualTo(GeoErrorCodes.AddressesUnavailable);
    }

    [Test]
    [Arguments("XX")]
    [Arguments("zz")]
    public async Task A_country_filter_that_is_no_ISO_country_is_invalid_and_never_reaches_the_provider(string country)
    {
        await using var harness = Create();

        var result = await harness.SuggestAsync("corrientes", country: country);

        await Assert.That(result.Errors.Single()).IsTypeOf<InvalidValueError>();
        await Assert.That(result.Errors.Single().Code).IsEqualTo(GeoErrorCodes.CountryCodeInvalidFormat);
        await Assert.That(harness.Provider.Searches).IsEmpty();
    }

    [Test]
    public async Task Without_a_configured_provider_both_answer_not_configured()
    {
        await using var harness = Create(provider: new UnconfiguredAddressProvider());

        var suggestions = await harness.SuggestAsync("corrientes");
        var resolved = await harness.ResolveAsync(Reference());

        await Assert.That(suggestions.Errors.Single()).IsTypeOf<UnavailableError>();
        await Assert.That(suggestions.Errors.Single().Code).IsEqualTo(GeoErrorCodes.AddressesNotConfigured);
        await Assert.That(resolved.Errors.Single()).IsTypeOf<UnavailableError>();
        await Assert.That(resolved.Errors.Single().Code).IsEqualTo(GeoErrorCodes.AddressesNotConfigured);
    }

    [Test]
    public async Task A_malformed_request_is_invalid_even_without_a_configured_provider()
    {
        await using var harness = Create(provider: new UnconfiguredAddressProvider());

        var suggestions = await harness.SuggestAsync("corrientes", session: "not-a-uuid", country: "XX");
        var resolved = await harness.ResolveAsync("not base64url!");

        await Assert.That(suggestions.Errors.Select(e => e.Code))
            .IsEquivalentTo([GeoErrorCodes.AddressSessionInvalid, GeoErrorCodes.CountryCodeInvalidFormat]);
        await Assert.That(resolved.Errors.Single().Code).IsEqualTo(GeoErrorCodes.AddressReferenceInvalid);
    }
}
