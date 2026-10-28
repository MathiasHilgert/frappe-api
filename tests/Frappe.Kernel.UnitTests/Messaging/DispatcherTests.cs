using Frappe.Kernel.Authorization;
using Frappe.Kernel.Messaging;
using NodaTime;

namespace Frappe.Kernel.UnitTests.Messaging;

public sealed class DispatcherTests
{
    private readonly PolicyRecordingDispatcher _dispatcher = new();

    [Fact]
    public async Task Invoking_a_command_exposes_its_policy_and_result_type_without_type_arguments()
    {
        _dispatcher.NextResult = 3;

        ErrorOr<int> result = await _dispatcher.InvokeAsync(
            new CountWidgets(),
            TestContext.Current.CancellationToken
        );

        _dispatcher.LastPolicy.ShouldBe(CountWidgets.Policy);
        result.Value.ShouldBe(3);
    }

    [Fact]
    public async Task Running_a_query_exposes_its_policy_and_result_type_without_type_arguments()
    {
        _dispatcher.NextResult = "gear";

        ErrorOr<string> result = await _dispatcher.QueryAsync(
            new FindWidgetName(),
            TestContext.Current.CancellationToken
        );

        _dispatcher.LastPolicy.ShouldBe(FindWidgetName.Policy);
        result.Value.ShouldBe("gear");
    }

    [Fact]
    public async Task Enqueuing_a_command_exposes_its_policy()
    {
        await _dispatcher.EnqueueAsync(new ArchiveWidgets(), TestContext.Current.CancellationToken);

        _dispatcher.LastPolicy.ShouldBe(ArchiveWidgets.Policy);
    }

    [Fact]
    public async Task Scheduling_a_command_exposes_its_policy()
    {
        await _dispatcher.ScheduleAsync(
            new ArchiveWidgets(),
            Instant.FromUnixTimeSeconds(0),
            TestContext.Current.CancellationToken
        );

        _dispatcher.LastPolicy.ShouldBe(ArchiveWidgets.Policy);
    }

    private sealed record CountWidgets : ICommand<CountWidgets, int>
    {
        public static Policy Policy { get; } = new("widget.count");
    }

    private sealed record ArchiveWidgets : ICommand<ArchiveWidgets>
    {
        public static Policy Policy { get; } = new("widget.archive");
    }

    private sealed record FindWidgetName : IQuery<FindWidgetName, string>
    {
        public static Policy Policy { get; } = new("widget.read");
    }

    // A real dispatcher reads the policy the same way: statically, from the concrete message type.
    private sealed class PolicyRecordingDispatcher : IDispatcher
    {
        public Policy? LastPolicy { get; private set; }

        public object? NextResult { get; set; }

        public Task<ErrorOr<TResult>> InvokeAsync<TCommand, TResult>(
            ICommand<TCommand, TResult> command,
            CancellationToken cancellationToken
        )
            where TCommand : ICommand<TCommand, TResult>
        {
            LastPolicy = TCommand.Policy;
            return Task.FromResult<ErrorOr<TResult>>((TResult)NextResult!);
        }

        public Task<ErrorOr<TResult>> QueryAsync<TQuery, TResult>(
            IQuery<TQuery, TResult> query,
            CancellationToken cancellationToken
        )
            where TQuery : IQuery<TQuery, TResult>
        {
            LastPolicy = TQuery.Policy;
            return Task.FromResult<ErrorOr<TResult>>((TResult)NextResult!);
        }

        public Task EnqueueAsync<TCommand>(TCommand command, CancellationToken cancellationToken)
            where TCommand : ICommand<TCommand>
        {
            LastPolicy = TCommand.Policy;
            return Task.CompletedTask;
        }

        public Task ScheduleAsync<TCommand>(
            TCommand command,
            Instant at,
            CancellationToken cancellationToken
        )
            where TCommand : ICommand<TCommand>
        {
            LastPolicy = TCommand.Policy;
            return Task.CompletedTask;
        }
    }
}
