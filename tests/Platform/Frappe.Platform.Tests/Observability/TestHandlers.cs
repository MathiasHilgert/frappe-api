using Frappe.Results;
using Frappe.UseCases;

namespace Frappe.Platform.Tests.Observability;

public sealed record Ping(string Marker) : IQuery<string>;

public sealed class PingHandler : IQueryHandler<Ping, string>
{
    public Task<Result<string>> HandleAsync(Ping query, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string>>($"pong:{query.Marker}");
}

public sealed record RejectedCommand(string Marker) : ICommand<Unit>;

public sealed class RejectedCommandHandler : ICommandHandler<RejectedCommand, Unit>
{
    public Task<Result<Unit>> HandleAsync(RejectedCommand command, CancellationToken cancellationToken) =>
        Task.FromResult<Result<Unit>>(Error.RuleViolation("test.rejected"));
}

public sealed record ExplodingCommand(string Marker) : ICommand<Unit>;

public sealed class ExplodingCommandHandler : ICommandHandler<ExplodingCommand, Unit>
{
    public Task<Result<Unit>> HandleAsync(ExplodingCommand command, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Infrastructure failure.");
}

public sealed record RejectedAtCommitCommand(string Marker) : ICommand<Unit>;

public sealed class RejectedAtCommitCommandHandler : ICommandHandler<RejectedAtCommitCommand, Unit>
{
    public Task<Result<Unit>> HandleAsync(RejectedAtCommitCommand command, CancellationToken cancellationToken) =>
        throw new LostRaceException();
}

/// <summary>What a storage throws when someone else changed the same data first: an expected answer, not an incident.</summary>
public sealed class LostRaceException : Exception, ICommitRejection
{
    public LostRaceException()
        : base("Someone else changed it first.")
    {
    }

    public LostRaceException(string message)
        : base(message)
    {
    }

    public LostRaceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public Error Answer { get; } = new ConflictError("test.changed_concurrently");
}

public sealed record SlowQuery(string Marker) : IQuery<string>;

public sealed class SlowQueryHandler : IQueryHandler<SlowQuery, string>
{
    public async Task<Result<string>> HandleAsync(SlowQuery query, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return "never";
    }
}

/// <summary>Generic, so assembly scanning skips it; tests register it by hand to simulate a second handler.</summary>
public sealed class ConflictingPingHandler<TMarker> : IQueryHandler<Ping, string>
{
    public Task<Result<string>> HandleAsync(Ping query, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string>>(typeof(TMarker).Name);
}

public sealed record AcceptedCommand(string Marker) : ICommand<string>;

public sealed class AcceptedCommandHandler : ICommandHandler<AcceptedCommand, string>
{
    public Task<Result<string>> HandleAsync(AcceptedCommand command, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string>>($"accepted:{command.Marker}");
}
