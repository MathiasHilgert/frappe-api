using Shouldly;

namespace Frappe.ArchitectureTests;

/// <summary>
/// CQRS lite: every state change is a command and every read a query, each a record message with a Wolverine handler
/// in the module's Application project, invoked from the endpoint through <c>IMessageBus</c>.
/// </summary>
public sealed class CqrsConventionTests
{
    public static TheoryData<string> Modules => [.. Solution.ModuleNames];

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Modules))]
    public void Message_IsARecordMarkedAsCommandOrQuery(string module) =>
        CqrsRules.UnmarkedMessages(Application(module)).ShouldBeEmpty(
            "Handled messages declared in Application must be records implementing ICommand or IQuery<T>.");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Modules))]
    public void Endpoint_ReachesApplicationOnlyThroughTheBus(string module) =>
        CqrsRules.EndpointsCallingServices(Application(module), Infrastructure(module)).ShouldBeEmpty(
            "Endpoints map HTTP to a message and send it with IMessageBus; they never call an Application service.");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Modules))]
    public void QueryHandler_NeverWrites(string module) =>
        CqrsRules.WritingQueryHandlers(Application(module)).ShouldBeEmpty(
            "Query handlers read through a read port; they never touch a repository, so they cannot save.");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Modules))]
    public void QueryHandler_IsNonTransactional(string module) =>
        CqrsRules.TransactionalQueryHandlers(Application(module)).ShouldBeEmpty(
            "Query handlers are marked [NonTransactional]: a read needs no EF transaction or outbox.");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Modules))]
    public void CommandHandler_ReturnsNoReadModel(string module) =>
        CqrsRules.CommandHandlersReturningReadModels(Application(module)).ShouldBeEmpty(
            "Command handlers answer with an id or a small outcome; the client reads back through a query.");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Modules))]
    public void Handler_ReturnsWhatItsMarkerDeclares(string module) =>
        HandlerAnswerRules.HandlersNotMatchingMarker(Application(module)).ShouldBeEmpty(
            "ICommand answers Result, ICommand<T> and IQuery<T> answer Result<T> (first in a tuple when cascading); a failure "
            + "travels as an Error, retry-after included. Background commands answer only with their cascading messages.");

    private static CqrsRules.Layer Application(string module) => Layer($"Frappe.{module}.Application");

    private static CqrsRules.Layer Infrastructure(string module) => Layer($"Frappe.{module}.Infrastructure");

    private static CqrsRules.Layer Layer(string name)
    {
        var project = Solution.Projects.Single(p => p.Name == name);
        return new CqrsRules.Layer(Solution.LoadAssembly(project), name);
    }
}
