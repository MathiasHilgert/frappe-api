using Frappe.ArchitectureTests.Fixtures.Application;
using Shouldly;

namespace Frappe.ArchitectureTests;

/// <summary>Each CQRS rule must catch the shape it forbids, named in its failure.</summary>
public sealed class CqrsRulesFailureTests
{
    private static readonly CqrsRules.Layer Application = new(typeof(CqrsRulesFailureTests).Assembly, "Frappe.ArchitectureTests.Fixtures.Application");
    private static readonly CqrsRules.Layer Infrastructure = new(typeof(CqrsRulesFailureTests).Assembly, "Frappe.ArchitectureTests.Fixtures.Infrastructure");

    [Fact]
    public void UnmarkedMessages_NamesAPlainClassMessage() =>
        CqrsRules.UnmarkedMessages(Application).ShouldBe([typeof(UnmarkedMessage).FullName!]);

    [Fact]
    public void EndpointsCallingServices_NamesTheEndpointUsingAService() =>
        CqrsRules.EndpointsCallingServices(Application, Infrastructure)
            .ShouldBe(["Frappe.ArchitectureTests.Fixtures.Infrastructure.ShortcutEndpoints"]);

    [Fact]
    public void WritingQueryHandlers_NamesAQueryHandlerUsingARepository() =>
        CqrsRules.WritingQueryHandlers(Application).ShouldBe([typeof(GetWidgetHandler).FullName!]);

    [Fact]
    public void CommandHandlersReturningReadModels_NamesACommandAnsweringWithAReadModel() =>
        CqrsRules.CommandHandlersReturningReadModels(Application)
            .ShouldBe([$"{typeof(RenameWidgetHandler).FullName}.Handle({nameof(RenameWidget)})"]);

    [Fact]
    public void CommandHandlersNotReturningResult_NamesEveryCommandAnsweringWithAnythingElse() =>
        CqrsRules.CommandHandlersNotReturningResult(Application).ShouldBe(
        [
            $"{typeof(RenameWidgetHandler).FullName}.Handle({nameof(RenameWidget)})",
            $"{typeof(ArchiveWidgetHandler).FullName}.Handle({nameof(ArchiveWidget)})",
        ], ignoreOrder: true);
}
