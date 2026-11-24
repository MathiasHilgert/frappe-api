using Frappe.Access.Contracts;
using Frappe.Access.Infrastructure;
using Frappe.Access.UnitTests.Fixtures;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Frappe.Access.UnitTests.Gate;

/// <summary>The check a host runs over its final container when it starts (S7): one marker per request and the gate in every handler chain.</summary>
public sealed class StartupCheckTests
{
    private static async Task<string?> StartAsync(Action<IServiceCollection> register, bool withCaller = true)
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddLogging();
        if (withCaller)
        {
            services.AddSingleton<ICaller>(Caller.Anonymous);
        }

        register(services);
        new AccessModule().AddServices(services);
        await using var provider = services.BuildServiceProvider();
        try
        {
            foreach (var check in provider.GetServices<IHostedService>())
            {
                await check.StartAsync(CancellationToken.None);
            }

            return null;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    private static void Gated<TCommand>(IServiceCollection services)
        where TCommand : ICommand<Unit> =>
        services.AddScoped<ICommandHandler<TCommand, Unit>>(provider => new GatedFake<TCommand>(
            new Handler<TCommand>(), provider.GetRequiredService<IUseCaseGate>()));

    [Test]
    public async Task A_request_with_no_marker_and_one_with_two_stop_the_host_naming_both()
    {
        // Act
        var message = await StartAsync(services =>
        {
            Gated<UnmarkedCommand>(services);
            Gated<BothMarkedCommand>(services);
            Gated<ChangeNoteCommand>(services);
        });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(message).Contains("UnmarkedCommand declares neither IGuarded nor IPublic");
            await Assert.That(message).Contains("BothMarkedCommand declares both IGuarded and IPublic");
            await Assert.That(message).DoesNotContain("ChangeNoteCommand");
        }
    }

    [Test]
    public async Task A_guarded_request_whose_rule_throws_stops_the_host_naming_it()
    {
        // Act
        var message = await StartAsync(Gated<UnreadableRuleCommand>);

        // Assert
        await Assert.That(message).Contains("UnreadableRuleCommand.Allowed cannot be read");
    }

    [Test]
    public async Task A_host_without_a_caller_stops_naming_what_it_lacks()
    {
        // Act
        var message = await StartAsync(services =>
        {
            foreach (var registered in services.Where(service => service.ServiceType == typeof(ICaller)).ToArray())
            {
                services.Remove(registered);
            }

            services.AddScoped<ICommandHandler<ChangeNoteCommand, Unit>>(_ => new Handler<ChangeNoteCommand>());
        }, withCaller: false);

        // Assert
        await Assert.That(message).Contains("needs an ICaller");
    }

    [Test]
    public async Task A_handler_chain_without_the_gate_stops_the_host_naming_its_request()
    {
        // Act
        var message = await StartAsync(services =>
        {
            Gated<ChangeNoteCommand>(services);
            services.AddScoped<ICommandHandler<EditNoteCommand, Unit>>(_ => new Handler<EditNoteCommand>());
        });

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(message).Contains("The handler of EditNoteCommand does not pass through the use-case gate");
            await Assert.That(message).DoesNotContain("ChangeNoteCommand");
        }
    }

    [Test]
    public async Task Marked_requests_whose_handlers_pass_through_the_gate_start_the_host()
    {
        // Act
        var message = await StartAsync(services =>
        {
            Gated<ChangeNoteCommand>(services);
            Gated<EditNoteCommand>(services);
        });

        // Assert
        await Assert.That(message).IsNull();
    }

    /// <summary>A decorator that holds the gate, as the platform's gated handler does.</summary>
    private sealed class GatedFake<TCommand>(ICommandHandler<TCommand, Unit> inner, IUseCaseGate gate) : ICommandHandler<TCommand, Unit>
        where TCommand : ICommand<Unit>
    {
        public Task<Result<Unit>> HandleAsync(TCommand command, CancellationToken cancellationToken) =>
            gate is null ? throw new InvalidOperationException() : inner.HandleAsync(command, cancellationToken);
    }

    /// <summary>The handler a chain ends in.</summary>
    private sealed class Handler<TCommand> : ICommandHandler<TCommand, Unit>
        where TCommand : ICommand<Unit>
    {
        public Task<Result<Unit>> HandleAsync(TCommand command, CancellationToken cancellationToken) => Task.FromResult<Result<Unit>>(Unit.Value);
    }
}
