using Shouldly;

namespace Frappe.ArchitectureTests;

/// <summary>
/// CQRS lite: every state change is a command and every read a query, each a record message with a Wolverine handler
/// in the module's Application project, invoked from the endpoint through <c>IMessageBus</c>.
/// </summary>
public sealed class CqrsConventionTests
{
    /// <summary>
    /// Application services endpoints still call directly, by module, until their slice moves to messages. Shrinks to
    /// empty as modules migrate; never add to it.
    /// </summary>
    private static readonly HashSet<string> NotYetMigrated = new(StringComparer.Ordinal)
    {
        "Frappe.Identity.Application.Account.ChangeLocale",
        "Frappe.Identity.Application.Account.ChangePassword",
        "Frappe.Identity.Application.Account.CompleteStepUp",
        "Frappe.Identity.Application.Account.ConfirmEmailChange",
        "Frappe.Identity.Application.Account.StepUpRequirement",
        "Frappe.Identity.Application.Owners.VerifyOwnerEmail",
        "Frappe.Identity.Application.Sessions.GetMe",
        "Frappe.Identity.Application.Sessions.LogIn",
        "Frappe.Identity.Application.Sessions.LogOut",
        "Frappe.Identity.Application.Sessions.OwnerSessions",
    };

    public static TheoryData<string> Modules => [.. Solution.ModuleNames];

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Modules))]
    public void Message_IsARecordMarkedAsCommandOrQuery(string module) =>
        CqrsRules.UnmarkedMessages(Application(module)).ShouldBeEmpty(
            "Handled messages declared in Application must be records implementing ICommand or IQuery<T>.");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Modules))]
    public void Endpoint_ReachesApplicationOnlyThroughTheBus(string module) =>
        CqrsRules.EndpointsCallingServices(Application(module), Infrastructure(module), NotYetMigrated).ShouldBeEmpty(
            "Endpoints map HTTP to a message and send it with IMessageBus; they never call an Application service.");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Modules))]
    public void QueryHandler_NeverWrites(string module) =>
        CqrsRules.WritingQueryHandlers(Application(module)).ShouldBeEmpty(
            "Query handlers read through a read port; they never touch a repository, so they cannot save.");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Modules))]
    public void CommandHandler_ReturnsNoReadModel(string module) =>
        CqrsRules.CommandHandlersReturningReadModels(Application(module)).ShouldBeEmpty(
            "Command handlers answer with an id or a small outcome; the client reads back through a query.");

    [Fact]
    public void NotYetMigrated_ListsOnlyServicesThatStillExist()
    {
        var services = Solution.ModuleNames
            .SelectMany(m => CqrsRules.Services(Application(m)))
            .Select(t => t.FullName)
            .ToHashSet(StringComparer.Ordinal);

        NotYetMigrated.Where(name => !services.Contains(name)).ShouldBeEmpty(
            "Remove migrated services from the allowlist.");
    }

    private static CqrsRules.Layer Application(string module) => Layer($"Frappe.{module}.Application");

    private static CqrsRules.Layer Infrastructure(string module) => Layer($"Frappe.{module}.Infrastructure");

    private static CqrsRules.Layer Layer(string name)
    {
        var project = Solution.Projects.Single(p => p.Name == name);
        return new CqrsRules.Layer(Solution.LoadAssembly(project), name);
    }
}
