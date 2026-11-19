using Frappe.Persistence;
using Frappe.UseCases;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Platform.UnitTests.Persistence;

/// <summary>Registering a module's unit of work: one unit per module key, and a unit registered earlier wins.</summary>
public sealed class UnitOfWorkRegistrationTests
{
    [Test]
    public async Task A_blank_module_is_refused()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var refusal = Assert.Throws<ArgumentException>(() => services.AddUnitOfWork<FirstContext>(" "));

        // Assert
        await Assert.That(refusal).IsNotNull();
    }

    [Test]
    public async Task Each_module_resolves_its_own_unit()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddScoped<FirstContext>(_ => new FirstContext())
            .AddScoped<SecondContext>(_ => new SecondContext())
            .AddUnitOfWork<FirstContext>("orders")
            .AddUnitOfWork<SecondContext>("billing");
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        // Act
        var orders = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>("orders");
        var billing = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>("billing");

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(orders).IsNotSameReferenceAs(billing);
            await Assert.That(orders.GetType().GenericTypeArguments).IsEquivalentTo([typeof(FirstContext)]);
            await Assert.That(billing.GetType().GenericTypeArguments).IsEquivalentTo([typeof(SecondContext)]);
        }
    }

    [Test]
    public async Task A_unit_registered_earlier_for_the_module_is_kept()
    {
        // Arrange
        var earlier = new StubUnit();
        var services = new ServiceCollection()
            .AddScoped<FirstContext>(_ => new FirstContext())
            .AddKeyedScoped<IUnitOfWork>("orders", (_, _) => earlier)
            .AddUnitOfWork<FirstContext>("orders");
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        // Act
        var resolved = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>("orders");

        // Assert
        await Assert.That(resolved).IsSameReferenceAs(earlier);
    }

    /// <summary>A module context.</summary>
    private sealed class FirstContext() : DbContext(new DbContextOptions<FirstContext>());

    /// <summary>Another module context.</summary>
    private sealed class SecondContext() : DbContext(new DbContextOptions<SecondContext>());

    /// <summary>A unit that does nothing.</summary>
    private sealed class StubUnit : IUnitOfWork
    {
        public Task BeginAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
