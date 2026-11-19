using Frappe.Aggregates;
using Frappe.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Platform.UnitTests.Observability;

/// <summary>
/// <c>CountOnCommit</c> registers the after-commit handler that counts a business success (OBS-06, OBS-08), so a module
/// states what it counts in one line and never writes a counting handler of its own.
/// </summary>
public sealed class CountOnCommitTests
{
    /// <summary>A readable fixed id: the same number always gives the same <see cref="Guid"/>.</summary>
    private static Guid FixedId(int number) => new(number, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>A domain event for a registration, replacing an earlier one or not.</summary>
    private sealed record Registered(Guid Id, bool Replaced) : IDomainEvent;

    /// <summary>A domain event for a closed registration.</summary>
    private sealed record Closed(Guid Id) : IDomainEvent;

    /// <summary>Records what was counted, in order.</summary>
    private sealed class Counters
    {
        public List<string> Counted { get; } = [];
    }

    private static ServiceProvider Services(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection().AddSingleton<Counters>();
        register(services);
        return services.BuildServiceProvider();
    }

    [Test]
    public async Task The_event_is_counted_when_its_after_commit_handlers_run_and_not_before()
    {
        // Arrange
        await using var services = Services(s => s.CountOnCommit<Registered, Counters>((counters, e) => counters.Counted.Add($"registered {e.Id}")));
        var id = FixedId(1);

        var handlers = services.GetServices<IAfterCommitDomainEventHandler<Registered>>().ToList();
        var before = services.GetRequiredService<Counters>().Counted.Count;

        // Act
        await handlers.Single().HandleAsync(new Registered(id, false), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(before).IsEqualTo(0);
            await Assert.That(services.GetRequiredService<Counters>().Counted).IsEquivalentTo([$"registered {id}"]);
        }
    }

    [Test]
    public async Task Several_counters_of_one_event_each_run()
    {
        // Arrange
        await using var services = Services(s => s
            .CountOnCommit<Registered, Counters>((counters, _) => counters.Counted.Add("registered"))
            .CountOnCommit<Registered, Counters>((counters, e) =>
            {
                if (e.Replaced)
                {
                    counters.Counted.Add("replaced");
                }
            }));

        // Act
        foreach (var handler in services.GetServices<IAfterCommitDomainEventHandler<Registered>>())
        {
            await handler.HandleAsync(new Registered(FixedId(2), true), CancellationToken.None);
        }

        // Assert
        await Assert.That(services.GetRequiredService<Counters>().Counted).IsEquivalentTo(["registered", "replaced"]);
    }

    [Test]
    public async Task A_counter_handles_only_its_event()
    {
        // Act
        await using var services = Services(s => s.CountOnCommit<Registered, Counters>((counters, _) => counters.Counted.Add("registered")));

        // Assert
        await Assert.That(services.GetServices<IAfterCommitDomainEventHandler<Closed>>()).IsEmpty();
    }

    [Test]
    public async Task The_instrument_is_the_one_the_container_holds()
    {
        // Arrange
        await using var services = Services(s => s.CountOnCommit<Registered, Counters>((counters, _) => counters.Counted.Add("x")));
        await using var scope = services.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredService<IAfterCommitDomainEventHandler<Registered>>()
            .HandleAsync(new Registered(FixedId(3), false), CancellationToken.None);

        // Assert
        await Assert.That(services.GetRequiredService<Counters>().Counted).Contains("x");
    }
}
