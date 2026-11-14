using Frappe.Observability;
using Frappe.Results;
using Frappe.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Platform.Tests.Observability;

/// <summary>
/// The transactional step of the handler pipeline: every command runs in its module's unit of work (module
/// <c>platform</c> here, from the namespace), committed only when its Result succeeds; queries never run in one.
/// </summary>
// Same key as HandlerPipelineTests: it captures every span of these handler types, so the two never run together.
[NotInParallel(nameof(HandlerPipelineTests))]
public sealed class UnitOfWorkPipelineTests
{
    private static ServiceProvider Services(RecordingUnitOfWork unitOfWork) =>
        new ServiceCollection()
            .AddLogging()
            .AddMetrics()
            .AddKeyedScoped<IUnitOfWork>("platform", (_, _) => unitOfWork)
            .AddHandlers(typeof(UnitOfWorkPipelineTests).Assembly)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    private static async Task<TResult> InScopeAsync<TResult>(ServiceProvider services, Func<IServiceProvider, Task<TResult>> run)
    {
        await using var scope = services.CreateAsyncScope();
        return await run(scope.ServiceProvider);
    }

    [Test]
    public async Task A_succeeding_command_is_committed_once()
    {
        var unitOfWork = new RecordingUnitOfWork();
        await using var services = Services(unitOfWork);

        var result = await InScopeAsync(services, provider => provider.GetRequiredService<ICommandHandler<AcceptedCommand, string>>()
            .HandleAsync(new AcceptedCommand("a"), CancellationToken.None));

        await Assert.That(result.Value).IsEqualTo("accepted:a");
        await Assert.That(string.Join(",", unitOfWork.Calls)).IsEqualTo("begin,commit");
    }

    [Test]
    public async Task A_rejected_command_is_rolled_back_and_keeps_its_result()
    {
        var unitOfWork = new RecordingUnitOfWork();
        await using var services = Services(unitOfWork);

        var result = await InScopeAsync(services, provider => provider.GetRequiredService<ICommandHandler<RejectedCommand, Unit>>()
            .HandleAsync(new RejectedCommand("r"), CancellationToken.None));

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors[0].Code).IsEqualTo("test.rejected");
        await Assert.That(string.Join(",", unitOfWork.Calls)).IsEqualTo("begin,rollback");
    }

    [Test]
    public async Task A_command_that_throws_is_rolled_back_and_the_exception_propagates()
    {
        var unitOfWork = new RecordingUnitOfWork();
        await using var services = Services(unitOfWork);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => InScopeAsync(
            services,
            provider => provider.GetRequiredService<ICommandHandler<ExplodingCommand, Unit>>().HandleAsync(new ExplodingCommand("x"), CancellationToken.None)));

        await Assert.That(exception!.Message).IsEqualTo("Infrastructure failure.");
        await Assert.That(string.Join(",", unitOfWork.Calls)).IsEqualTo("begin,rollback");
    }

    [Test]
    public async Task A_failed_commit_is_rolled_back_and_the_exception_propagates()
    {
        var unitOfWork = new RecordingUnitOfWork { FailCommit = true };
        await using var services = Services(unitOfWork);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => InScopeAsync(
            services,
            provider => provider.GetRequiredService<ICommandHandler<AcceptedCommand, string>>().HandleAsync(new AcceptedCommand("c"), CancellationToken.None)));

        await Assert.That(exception!.Message).IsEqualTo("The database refused the commit.");
        await Assert.That(string.Join(",", unitOfWork.Calls)).IsEqualTo("begin,commit,rollback");
    }

    [Test]
    public async Task A_query_never_touches_the_unit_of_work()
    {
        var unitOfWork = new RecordingUnitOfWork();
        await using var services = Services(unitOfWork);

        var result = await InScopeAsync(services, provider => provider.GetRequiredService<IQueryHandler<Ping, string>>()
            .HandleAsync(new Ping("q"), CancellationToken.None));

        await Assert.That(result.Value).IsEqualTo("pong:q");
        await Assert.That(unitOfWork.Calls).IsEmpty();
    }

    [Test]
    public async Task A_command_of_a_module_without_a_unit_of_work_fails_naming_the_module_instead_of_saving_nothing()
    {
        // A module that forgot AddUnitOfWork would otherwise lose every write in silence.
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddMetrics()
            .AddKeyedScoped<IUnitOfWork>("another-module", (_, _) => new RecordingUnitOfWork())
            .AddHandlers(typeof(UnitOfWorkPipelineTests).Assembly)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        async Task RunAsync()
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ICommandHandler<AcceptedCommand, string>>()
                .HandleAsync(new AcceptedCommand("n"), CancellationToken.None);
        }

        var failure = await Assert.That(RunAsync).Throws<InvalidOperationException>();

        await Assert.That(failure!.Message).Contains("'platform'");
    }
}
