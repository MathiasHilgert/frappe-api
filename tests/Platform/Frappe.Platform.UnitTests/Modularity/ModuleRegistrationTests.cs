using Frappe.Platform.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.UnitTests.Modularity;

public sealed class ModuleRegistrationTests
{
    [Fact]
    public void Valid_modules_add_their_services_and_are_registered_in_order()
    {
        var geo = StubModule.Named(
            "geo",
            eventIds: new(1000, 1999),
            published: [typeof(TableOpened)]
        );
        var diner = StubModule.Named(
            "diner",
            eventIds: new(2000, 2999),
            subscribed: [typeof(TableOpened)]
        );
        HostApplicationBuilder builder = NewBuilder();

        builder.AddModules(geo, diner);

        geo.ServicesAdded.ShouldBeTrue();
        diner.ServicesAdded.ShouldBeTrue();
        using ServiceProvider services = builder.Services.BuildServiceProvider();
        services.GetServices<IModule>().ShouldBe([geo, diner]);
    }

    [Fact]
    public void A_null_module_is_rejected()
    {
        HostApplicationBuilder builder = NewBuilder();

        Should.Throw<ArgumentException>(() => builder.AddModules(StubModule.Named("geo"), null!));
    }

    [Fact]
    public void Adding_modules_a_second_time_is_rejected()
    {
        HostApplicationBuilder builder = NewBuilder();
        builder.AddModules(StubModule.Named("geo"));

        Should
            .Throw<InvalidOperationException>(() => builder.AddModules())
            .Message.ShouldContain("already added");
    }

    [Fact]
    public void Two_modules_with_one_name_are_rejected()
    {
        ShouldReject(
            "name 'geo'",
            StubModule.Named(
                "geo",
                schema: "geo_a",
                routePrefix: "geo-a",
                eventIds: new(1000, 1999)
            ),
            StubModule.Named(
                "geo",
                schema: "geo_b",
                routePrefix: "geo-b",
                eventIds: new(2000, 2999)
            )
        );
    }

    [Fact]
    public void Two_modules_with_one_schema_are_rejected()
    {
        ShouldReject(
            "schema 'shared'",
            StubModule.Named("geo", schema: "shared", eventIds: new(1000, 1999)),
            StubModule.Named("diner", schema: "shared", eventIds: new(2000, 2999))
        );
    }

    [Theory]
    [InlineData("platform")]
    [InlineData("wolverine")]
    [InlineData("quartz")]
    [InlineData("public")]
    [InlineData("information_schema")]
    [InlineData("pg_catalog")]
    [InlineData("pg_temp")]
    public void A_schema_the_platform_or_postgres_owns_is_rejected(string schema)
    {
        ShouldReject($"schema '{schema}'", StubModule.Named("geo", schema: schema));
    }

    [Fact]
    public void Two_modules_with_one_route_prefix_are_rejected()
    {
        ShouldReject(
            "route prefix 'places'",
            StubModule.Named("geo", routePrefix: "places", eventIds: new(1000, 1999)),
            StubModule.Named("diner", routePrefix: "places", eventIds: new(2000, 2999))
        );
    }

    [Fact]
    public void Overlapping_event_id_ranges_are_rejected()
    {
        ShouldReject(
            "1000-1999",
            StubModule.Named("geo", eventIds: new(1000, 1999)),
            StubModule.Named("diner", eventIds: new(1999, 2999))
        );
    }

    [Fact]
    public void A_subscription_to_an_event_no_module_publishes_is_rejected()
    {
        ShouldReject(
            nameof(TableClosed),
            StubModule.Named("geo", eventIds: new(1000, 1999), published: [typeof(TableOpened)]),
            StubModule.Named("diner", eventIds: new(2000, 2999), subscribed: [typeof(TableClosed)])
        );
    }

    [Fact]
    public void An_event_two_modules_publish_is_rejected()
    {
        ShouldReject(
            $"Modules 'geo', 'diner' publish {typeof(TableOpened).FullName}",
            StubModule.Named("geo", eventIds: new(1000, 1999), published: [typeof(TableOpened)]),
            StubModule.Named("diner", eventIds: new(2000, 2999), published: [typeof(TableOpened)])
        );
    }

    [Fact]
    public void A_permission_two_modules_declare_is_rejected()
    {
        ShouldReject(
            "share permission 'geo.zone.read'",
            StubModule.Named(
                "geo",
                schema: "geo_a",
                routePrefix: "geo-a",
                eventIds: new(1000, 1999),
                permissions: ["geo.zone.read"]
            ),
            StubModule.Named(
                "geo",
                schema: "geo_b",
                routePrefix: "geo-b",
                eventIds: new(2000, 2999),
                permissions: ["geo.zone.read"]
            )
        );
    }

    [Fact]
    public void A_retry_after_a_failed_check_reports_the_violations_again()
    {
        HostApplicationBuilder builder = NewBuilder();
        IModule[] modules = [StubModule.Named("geo", schema: "quartz")];
        Should.Throw<InvalidOperationException>(() => builder.AddModules(modules));

        Should
            .Throw<InvalidOperationException>(() => builder.AddModules(modules))
            .Message.ShouldContain("schema 'quartz'");
    }

    [Fact]
    public void Every_violation_is_reported_in_one_exception_before_any_service_is_added()
    {
        var geo = StubModule.Named(
            "geo",
            schema: "quartz",
            routePrefix: "places",
            eventIds: new(1000, 1999)
        );
        var diner = StubModule.Named(
            "diner",
            routePrefix: "places",
            eventIds: new(1500, 2999),
            subscribed: [typeof(TableClosed)]
        );

        InvalidOperationException error = Should.Throw<InvalidOperationException>(() =>
            NewBuilder().AddModules(geo, diner)
        );

        error.Message.ShouldContain("schema 'quartz'");
        error.Message.ShouldContain("route prefix 'places'");
        error.Message.ShouldContain("1000-1999");
        error.Message.ShouldContain(nameof(TableClosed));
        geo.ServicesAdded.ShouldBeFalse();
        diner.ServicesAdded.ShouldBeFalse();
    }

    private static void ShouldReject(string violation, params IModule[] modules)
    {
        Should
            .Throw<InvalidOperationException>(() => NewBuilder().AddModules(modules))
            .Message.ShouldContain(violation);
    }

    private static HostApplicationBuilder NewBuilder() =>
        Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true }
        );
}
