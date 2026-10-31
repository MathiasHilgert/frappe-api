using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Frappe.Geo.Infrastructure.Countries;
using Frappe.Geo.Infrastructure.Currencies;
using Frappe.Geo.Infrastructure.Locales;
using Frappe.Geo.Infrastructure.Subdivisions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using NodaTime.TimeZones;

namespace Frappe.Geo.Infrastructure.Catalogues;

/// <summary>
/// Loads the database-backed catalogues before the host serves anything: in <see cref="StartingAsync"/>, ahead of the
/// message listeners and Kestrel, so a dead database fails the start instead of the first request. Then it fixes the
/// dataset tag every catalogue response carries.
/// </summary>
internal sealed class GeoCatalogueLoader(
    IServiceScopeFactory scopes,
    CurrencySnapshot currencies,
    CountrySnapshot countries,
    SubdivisionSnapshot subdivisions,
    LocaleSnapshot locales,
    ILogger<GeoCatalogueLoader> logger
) : IHostedLifecycleService
{
    private EntityTagHeaderValue? _datasetTag;

    /// <summary>Gets the tag of the loaded dataset, the same for every catalogue response of this process.</summary>
    public EntityTagHeaderValue DatasetTag =>
        _datasetTag ?? throw new InvalidOperationException("The geo catalogues are not loaded.");

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        GeoDbContext db = scope.ServiceProvider.GetRequiredService<GeoDbContext>();
        LoadedCatalogue loadedCurrencies = await currencies.LoadAsync(db, cancellationToken);
        Log.CatalogueLoaded(logger, "currencies", loadedCurrencies.Rows);
        LoadedCatalogue loadedCountries = await countries.LoadAsync(db, cancellationToken);
        Log.CatalogueLoaded(logger, "countries", loadedCountries.Rows);
        LoadedCatalogue loadedSubdivisions = await subdivisions.LoadAsync(db, cancellationToken);
        Log.CatalogueLoaded(logger, "subdivisions", loadedSubdivisions.Rows);
        LoadedCatalogue loadedLocales = await locales.LoadAsync(db, cancellationToken);
        Log.CatalogueLoaded(logger, "locales", loadedLocales.Rows);
        _datasetTag = Tag(
            [
                .. loadedCurrencies.Lines,
                .. loadedCountries.Lines,
                .. loadedSubdivisions.Lines,
                .. loadedLocales.Lines,
            ],
            Assembly
                .GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?? "",
            TzdbDateTimeZoneSource.Default.TzdbVersion
        );
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Hashes the loaded data with the build and the tzdb, so a changed row, a new build (response shapes, value
    /// objects, platform) or a tzdb update each yield a new tag.
    /// </summary>
    internal static EntityTagHeaderValue Tag(
        IReadOnlyList<string> lines,
        string build,
        string tzdbVersion
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string line in lines.Append(build).Append(tzdbVersion))
        {
            // The line feed ends each entry, so moving a character between entries changes the hash.
            hash.AppendData(Encoding.UTF8.GetBytes(line + "\n"));
        }

        return new EntityTagHeaderValue($"\"{Convert.ToHexStringLower(hash.GetHashAndReset())}\"");
    }
}
