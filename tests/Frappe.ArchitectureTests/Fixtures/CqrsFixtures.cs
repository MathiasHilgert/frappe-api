// Deliberately broken shapes for CqrsRulesFailureTests; nothing outside those tests uses them.
#pragma warning disable CA1822, S1186, S2325, S3903

using Frappe.Application.Abstractions.Messaging;
using Frappe.ArchitectureTests.Fixtures.Application;

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
