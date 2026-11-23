using System.Diagnostics;
using Frappe.Aggregates;
using Frappe.IntegrationEvents;
using Frappe.Messaging;
using Frappe.Messaging.Outbox;
using Frappe.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;

namespace Frappe.Platform.IntegrationTests.Messaging;

/// <summary>
/// A module context that opted in with <c>UseOutbox</c> writes the integration events its aggregates raised in the same
/// <c>SaveChanges</c> as the aggregates, translated by the module's keyed translator, and writes nothing otherwise.
/// </summary>
[ClassDataSource<InboxDatabase>(Shared = SharedType.PerTestSession)]
public sealed class OutboxInterceptorTests(InboxDatabase database)
{
    private const string Module = "widgets";

    /// <summary>The integration event a made widget produces.</summary>
    [IntegrationEvent("frappe.widgets.widget_made.v1")]
    private sealed record WidgetMade(Guid WidgetId, string Name) : IIntegrationEvent;

    /// <summary>An integration event that forgot its type attribute.</summary>
    private sealed record Unnamed : IIntegrationEvent;

    /// <summary>A widget was made; the module shows it to others.</summary>
    private sealed record WidgetWasMade(Guid WidgetId, string Name) : IDomainEvent;

    /// <summary>A widget was tidied; nobody outside the module cares.</summary>
    private sealed record WidgetWasTidied : IDomainEvent;

    /// <summary>A domain event the translator turns into an event without a type.</summary>
    private sealed record WidgetWasBroken : IDomainEvent;

    /// <summary>The aggregate that records what happened to it.</summary>
    private sealed class Widget : AggregateRoot
    {
        private Widget()
        {
            Name = null!;
        }

        public Widget(string name)
        {
            Id = Guid.NewGuid();
            Name = name;
        }

        public Guid Id { get; private set; }

        public string Name { get; private set; }

        public void Make() => Raise(new WidgetWasMade(Id, Name));

        public void Tidy() => Raise(new WidgetWasTidied());

        public void Break() => Raise(new WidgetWasBroken());
    }

    /// <summary>Publishes only what the module wants others to see.</summary>
    private sealed class WidgetTranslator : IIntegrationEventTranslator
    {
        public IEnumerable<IIntegrationEvent> Translate(IDomainEvent domainEvent) => domainEvent switch
        {
            WidgetWasMade made => [new WidgetMade(made.WidgetId, made.Name)],
            WidgetWasBroken => [new Unnamed()],
            _ => [],
        };
    }

    /// <summary>A module context with an outbox, as a module registers it.</summary>
    private sealed class WidgetContext(DbContextOptions<WidgetContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("module");
            modelBuilder.AddOutbox();
            modelBuilder.Entity<Widget>().ToTable("widgets").HasKey(w => w.Id);
            modelBuilder.Entity<Widget>().Ignore(w => w.DomainEvents);
        }
    }

    private async Task<ServiceProvider> ServicesAsync()
    {
        var connection = database.NewConnectionString();
        var services = new ServiceCollection()
            .AddSingleton<IClock>(new TimeProviderClock(TimeProvider.System))
            .AddKeyedSingleton<IIntegrationEventTranslator, WidgetTranslator>(Module)
            .AddDbContext<WidgetContext>((provider, options) =>
                options.UseNpgsql(connection, npgsql => npgsql.UseNodaTime()).UseOutbox(provider, Module))
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WidgetContext>().Database.EnsureCreatedAsync();
        return services;
    }

    private static async Task<(int Widgets, List<OutboxMessage> Outbox)> StoredAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WidgetContext>();
        return (await db.Set<Widget>().CountAsync(), await db.Set<OutboxMessage>().ToListAsync());
    }

    [Test]
    public async Task The_translated_event_is_written_to_the_outbox_by_the_same_save_as_the_aggregate()
    {
        // Arrange
        await using var services = await ServicesAsync();
        var widget = new Widget("sprocket");
        widget.Make();
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WidgetContext>();
            db.Add(widget);

            // Act
            await db.SaveChangesAsync();
        }

        // Assert
        var (widgets, outbox) = await StoredAsync(services);
        var message = outbox.Single();
        using (Assert.Multiple())
        {
            await Assert.That(widgets).IsEqualTo(1);
            await Assert.That(message.Type).IsEqualTo("frappe.widgets.widget_made.v1");
            await Assert.That(message.Source).IsEqualTo("/frappe/widgets");
            await Assert.That(message.DataContentType).IsEqualTo("application/json");
            await Assert.That(message.Data).Contains(widget.Id.ToString()).And.Contains("sprocket");
            await Assert.That(message.PublishedAt).IsNull();
            await Assert.That(widget.DomainEvents).IsEmpty();
        }
    }

    [Test]
    public async Task The_synchronous_save_writes_the_outbox_row_too()
    {
        // Arrange
        await using var services = await ServicesAsync();
        var widget = new Widget("gear");
        widget.Make();
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WidgetContext>();
            db.Add(widget);

            // Act
#pragma warning disable S6966 // The synchronous save is the behavior under test.
            db.SaveChanges();
#pragma warning restore S6966
        }

        // Assert
        var (_, outbox) = await StoredAsync(services);
        await Assert.That(outbox.Select(m => m.Type)).IsEquivalentTo(["frappe.widgets.widget_made.v1"]);
    }

    [Test]
    public async Task A_domain_event_the_translator_does_not_map_writes_no_outbox_row()
    {
        // Arrange
        await using var services = await ServicesAsync();
        var widget = new Widget("cog");
        widget.Tidy();
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WidgetContext>();
            db.Add(widget);

            // Act
            await db.SaveChangesAsync();
        }

        // Assert
        var (widgets, outbox) = await StoredAsync(services);
        using (Assert.Multiple())
        {
            await Assert.That(widgets).IsEqualTo(1);
            await Assert.That(outbox).IsEmpty();
        }
    }

    [Test]
    public async Task An_aggregate_without_events_writes_no_outbox_row()
    {
        // Arrange
        await using var services = await ServicesAsync();
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WidgetContext>();
            db.Add(new Widget("nut"));

            // Act
            await db.SaveChangesAsync();
        }

        // Assert
        var (_, outbox) = await StoredAsync(services);
        await Assert.That(outbox).IsEmpty();
    }

    [Test]
    public async Task An_integration_event_without_a_type_fails_the_save_and_keeps_the_events_on_the_aggregate()
    {
        // Arrange
        await using var services = await ServicesAsync();
        var widget = new Widget("bolt");
        widget.Break();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WidgetContext>();
        db.Add(widget);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure!.Message).Contains("IntegrationEvent");
            await Assert.That(widget.DomainEvents).Count().IsEqualTo(1);
        }
    }

    [Test]
    public async Task The_trace_context_of_the_save_is_stored_with_the_outbox_row()
    {
        // Arrange
        await using var services = await ServicesAsync();
        var widget = new Widget("washer");
        widget.Make();
        using var activity = new Activity("save").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.TraceStateString = "vendor=value";
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WidgetContext>();
            db.Add(widget);

            // Act
            await db.SaveChangesAsync();
        }

        // Assert
        var message = (await StoredAsync(services)).Outbox.Single();
        using (Assert.Multiple())
        {
            await Assert.That(message.TraceParent).IsEqualTo(activity.Id);
            await Assert.That(message.TraceState).IsEqualTo("vendor=value");
        }
    }

    [Test]
    public async Task A_trace_state_longer_than_the_stored_limit_is_dropped_instead_of_truncated()
    {
        // Arrange
        await using var services = await ServicesAsync();
        var widget = new Widget("rivet");
        widget.Make();
        using var activity = new Activity("save").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.TraceStateString = string.Join(',', Enumerable.Range(0, 40).Select(i => $"vendor{i}=" + new string('v', 20)));
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WidgetContext>();
            db.Add(widget);

            // Act
            await db.SaveChangesAsync();
        }

        // Assert
        var message = (await StoredAsync(services)).Outbox.Single();
        using (Assert.Multiple())
        {
            await Assert.That(activity.TraceStateString.Length).IsGreaterThan(OutboxModel.MaxTraceStateLength);
            await Assert.That(message.TraceParent).IsEqualTo(activity.Id);
            await Assert.That(message.TraceState).IsNull();
        }
    }
}
