using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Frappe.Geo.IntegrationTests;

// Geo is written only by the migrator's seed: the app role reads its tables and the module serves only reads.
public sealed class GeoCatalogTests(GeoFixture geo)
{
    private static readonly string[] _selectOnly =
    [
        "countries:SELECT",
        "country_names:SELECT",
        "currencies:SELECT",
        "currency_names:SELECT",
    ];

    [Fact]
    public async Task The_app_role_holds_only_select_on_every_geo_table()
    {
        await using NpgsqlConnection connection = new(geo.OwnerConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = new(
            """
            SELECT coalesce(array_agg(DISTINCT t.tablename || ':' || p.privilege ORDER BY t.tablename || ':' || p.privilege), '{}')
            FROM pg_tables t
            CROSS JOIN unnest(ARRAY['SELECT', 'INSERT', 'UPDATE', 'DELETE', 'TRUNCATE', 'REFERENCES', 'TRIGGER']) AS p(privilege)
            WHERE t.schemaname = 'geo' AND t.tablename <> '__ef_migrations_history'
              AND has_table_privilege('frappe_app', format('%I.%I', t.schemaname, t.tablename), p.privilege)
            """,
            connection
        );

        object? granted = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        granted.ShouldBe(_selectOnly);
    }

    [Fact]
    public void Geo_maps_only_get_endpoints()
    {
        string[] methods =
        [
            .. geo
                .Host.Services.GetRequiredService<EndpointDataSource>()
                .Endpoints.OfType<RouteEndpoint>()
                .Where(static endpoint =>
                    endpoint.RoutePattern.RawText?.StartsWith("/v1/geo/", StringComparison.Ordinal)
                    == true
                )
                .SelectMany(static endpoint =>
                    endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []
                )
                .Distinct(StringComparer.Ordinal),
        ];

        methods.ShouldBe([HttpMethods.Get]);
    }
}
