// Deliberately broken shapes for CqrsRulesFailureTests; nothing outside those tests uses them.
#pragma warning disable CA1822, S1186, S2325, S3903

using Frappe.Application.Abstractions.Messaging;
using Frappe.ArchitectureTests.Fixtures.Application;
using Frappe.SharedKernel.Validation;
using Wolverine;

namespace Frappe.ArchitectureTests.Fixtures.Application
{
    public sealed record WidgetView(Guid Id, string Name);

    public interface IWidgetRepository
    {
        Task SaveChangesAsync();
    }

    public sealed record GetWidget(Guid Id) : IQuery<WidgetView?>;

    public static class GetWidgetHandler
    {
        public static Task<WidgetView?> Handle(GetWidget query, IWidgetRepository repository) =>
            Task.FromResult<WidgetView?>(null);
    }

    public sealed record RenameWidget(Guid Id, string Name) : ICommand;

    public static class RenameWidgetHandler
    {
        public static Task<WidgetView> Handle(RenameWidget command) => Task.FromResult(new WidgetView(command.Id, command.Name));
    }

    public sealed record ArchiveWidget(Guid Id) : ICommand;

    public sealed record ArchiveOutcome(bool Archived, TimeSpan RetryAfter);

    public static class ArchiveWidgetHandler
    {
        public static Task<ArchiveOutcome> Handle(ArchiveWidget command) => Task.FromResult(new ArchiveOutcome(true, TimeSpan.Zero));
    }

    public sealed record PublishWidget(Guid Id) : ICommand<Guid>;

    public static class PublishWidgetHandler
    {
        public static Task<(Result<Guid>, OutgoingMessages)> Handle(PublishWidget command) =>
            Task.FromResult((Result.Success(command.Id), new OutgoingMessages()));
    }

    public sealed record ForgetWidget(Guid Id) : ICommand;

    public static class ForgetWidgetHandler
    {
        public static ValueTask<Result> Handle(ForgetWidget command) => ValueTask.FromResult(Result.Success());
    }

    public sealed record CountWidgets : ICommand<int>;

    public static class CountWidgetsHandler
    {
        public static Result<Guid> Handle(CountWidgets command) => Result.Success(Guid.Empty);
    }

    public sealed record FindWidget(Guid Id) : IQuery<WidgetView>;

    [Wolverine.Attributes.NonTransactional]
    public static class FindWidgetHandler
    {
        public static Task<Result<WidgetView>> Handle(FindWidget query) =>
            Task.FromResult(Result.Success(new WidgetView(query.Id, string.Empty)));
    }

    public sealed record SweepWidgets : IBackgroundCommand;

    public static class SweepWidgetsHandler
    {
        public static Task<OutgoingMessages> Handle(SweepWidgets command) => Task.FromResult(new OutgoingMessages());
    }

    public sealed record ReindexWidgets : IBackgroundCommand;

    public static class ReindexWidgetsHandler
    {
        public static Task<(Result, OutgoingMessages)> Handle(ReindexWidgets command) =>
            Task.FromResult((Result.Success(), new OutgoingMessages()));
    }

    public sealed class UnmarkedMessage;

    public static class UnmarkedMessageHandler
    {
        public static void Handle(UnmarkedMessage message)
        {
        }
    }

    public sealed class GetWidgetService
    {
        public WidgetView Get(Guid id) => new(id, string.Empty);
    }
}

namespace Frappe.ArchitectureTests.Fixtures.Infrastructure
{
    public static class ShortcutEndpoints
    {
        public static WidgetView Get(Guid id, GetWidgetService service) => service.Get(id);
    }

    public static class BusEndpoints
    {
        public static GetWidget Get(Guid id) => new(id);
    }
}
