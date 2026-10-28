using Frappe.Platform.Modularity;

namespace Frappe.Platform.UnitTests.Modularity;

public sealed class ModuleMetadataTests
{
    [Theory]
    [InlineData("Geo")]
    [InlineData("geo-zones")]
    [InlineData("1geo")]
    [InlineData(" ")]
    public void A_name_that_is_not_a_lowercase_identifier_is_rejected(string name)
    {
        Should.Throw<ArgumentException>(() =>
            StubModule.Named(name, schema: "geo", routePrefix: "geo")
        );
    }

    [Theory]
    [InlineData("Geo")]
    [InlineData("geo-zones")]
    public void A_schema_that_is_not_a_lowercase_identifier_is_rejected(string schema)
    {
        Should.Throw<ArgumentException>(() => StubModule.Named("geo", schema: schema));
    }

    [Theory]
    [InlineData("Cash-Shifts")]
    [InlineData("cash_shifts")]
    [InlineData("cash-")]
    [InlineData("v1/geo")]
    public void A_route_prefix_that_is_not_lowercase_kebab_case_is_rejected(string routePrefix)
    {
        Should.Throw<ArgumentException>(() => StubModule.Named("geo", routePrefix: routePrefix));
    }

    [Fact]
    public void Lowercase_identifiers_and_a_kebab_case_prefix_are_accepted()
    {
        ModuleMetadata metadata = StubModule
            .Named("cash_box", schema: "cash_box", routePrefix: "cash-shifts")
            .Metadata;

        metadata.Name.ShouldBe("cash_box");
        metadata.RoutePrefix.ShouldBe("cash-shifts");
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-5, 10)]
    [InlineData(20, 10)]
    public void An_event_id_range_that_is_empty_or_starts_at_zero_is_rejected(int start, int end)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new EventIdRange(start, end));
    }

    [Theory]
    [InlineData(1000, 1999, 1999, 2999, true)]
    [InlineData(1000, 1999, 2000, 2999, false)]
    [InlineData(1000, 2999, 1500, 1600, true)]
    public void Event_id_ranges_overlap_when_they_share_an_event_id(
        int start,
        int end,
        int otherStart,
        int otherEnd,
        bool overlaps
    )
    {
        new EventIdRange(start, end)
            .Overlaps(new EventIdRange(otherStart, otherEnd))
            .ShouldBe(overlaps);
        new EventIdRange(otherStart, otherEnd)
            .Overlaps(new EventIdRange(start, end))
            .ShouldBe(overlaps);
    }

    [Theory]
    [InlineData("Geo.Zone.Read")]
    [InlineData("geo")]
    [InlineData("$self")]
    [InlineData("diner.tab.open")]
    [InlineData("geography.zone.read")]
    public void A_permission_outside_the_policy_format_or_another_modules_namespace_is_rejected(
        string permission
    )
    {
        Should.Throw<ArgumentException>(() =>
            new ModuleMetadata(
                "geo",
                "geo",
                "geo",
                new EventIdRange(1000, 1999),
                [permission],
                [],
                []
            )
        );
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(Frappe.Kernel.Messaging.IIntegrationEvent))]
    public void A_published_or_subscribed_type_that_is_not_a_concrete_integration_event_is_rejected(
        Type type
    )
    {
        Should.Throw<ArgumentException>(() => StubModule.Named("geo", published: [type]));
        Should.Throw<ArgumentException>(() => StubModule.Named("geo", subscribed: [type]));
    }

    [Fact]
    public void The_declared_collections_are_copied_so_a_later_change_to_the_source_is_not_seen()
    {
        List<string> permissions = ["geo.zone.read"];
        List<Type> published = [typeof(TableOpened)];
        List<Type> subscribed = [typeof(TableClosed)];
        var metadata = new ModuleMetadata(
            "geo",
            "geo",
            "geo",
            new EventIdRange(1000, 1999),
            permissions,
            published,
            subscribed
        );

        permissions.Add("geo.zone.write");
        published.Add(typeof(TableClosed));
        subscribed.Clear();

        metadata.Permissions.ShouldBe(["geo.zone.read"]);
        metadata.PublishedEvents.ShouldBe([typeof(TableOpened)]);
        metadata.SubscribedEvents.ShouldBe([typeof(TableClosed)]);
    }
}
