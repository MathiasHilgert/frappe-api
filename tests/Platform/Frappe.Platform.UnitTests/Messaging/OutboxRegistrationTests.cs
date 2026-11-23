using Frappe.Aggregates;
using Frappe.IntegrationEvents;
using Frappe.Messaging.Outbox;
using Frappe.Persistence;
using Frappe.Platform.Testing.Messaging;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.UnitTests.Messaging;

/// <summary>A module gets its outbox from the callback of <c>AddModuleDatabase</c>, without chaining it itself.</summary>
public sealed class OutboxRegistrationTests
{
    /// <summary>A translator that turns no domain event into an integration event.</summary>
    private sealed class NoTranslation : IIntegrationEventTranslator
    {
        public IEnumerable<IIntegrationEvent> Translate(IDomainEvent domainEvent) => [];
    }

    [Test]
    public async Task The_callback_of_the_module_database_contributes_the_outbox_interceptor()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Test"] = "Host=db;Database=frappe;Username=frappe_test;Password=secret",
        });
        builder.Services.AddKeyedSingleton<IIntegrationEventTranslator, NoTranslation>("test");
        builder.Services.AddModuleDatabase<InboxTestContext>("test", "module", (provider, options) => options.UseOutbox(provider, "test"));
        using var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();

        // Act
        var interceptors = scope.ServiceProvider.GetRequiredService<InboxTestContext>().GetService<IDbContextOptions>()
            .Extensions.OfType<CoreOptionsExtension>().Single().Interceptors ?? [];

        // Assert
        await Assert.That(interceptors.OfType<OutboxInterceptor>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Each_module_gets_the_outbox_with_its_own_translator()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:First"] = "Host=db;Database=frappe;Username=frappe_test;Password=secret",
            ["ConnectionStrings:Second"] = "Host=db;Database=frappe;Username=frappe_test;Password=secret",
        });
        var first = new NoTranslation();
        var second = new NoTranslation();
        builder.Services.AddKeyedSingleton<IIntegrationEventTranslator>("first", first);
        builder.Services.AddKeyedSingleton<IIntegrationEventTranslator>("second", second);
        builder.Services.AddModuleDatabase<InboxTestContext>("first", "module", (provider, options) => options.UseOutbox(provider, "first"));
        builder.Services.AddModuleDatabase<OtherContext>("second", "module", (provider, options) => options.UseOutbox(provider, "second"));
        using var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();

        // Act
        var firstTranslator = InterceptorOf(scope.ServiceProvider.GetRequiredService<InboxTestContext>()).Translator;
        var secondTranslator = InterceptorOf(scope.ServiceProvider.GetRequiredService<OtherContext>()).Translator;

        // Assert
        await Assert.That(firstTranslator).IsSameReferenceAs(first);
        await Assert.That(secondTranslator).IsSameReferenceAs(second);
    }

    private static OutboxInterceptor InterceptorOf(Microsoft.EntityFrameworkCore.DbContext context) =>
        (context.GetService<IDbContextOptions>().Extensions.OfType<CoreOptionsExtension>().Single().Interceptors ?? [])
        .OfType<OutboxInterceptor>().Single();

    /// <summary>A second module context.</summary>
    private sealed class OtherContext(Microsoft.EntityFrameworkCore.DbContextOptions<OtherContext> options)
        : Microsoft.EntityFrameworkCore.DbContext(options);
}
