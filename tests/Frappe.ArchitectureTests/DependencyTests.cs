using NetArchTest.Rules;
using Shouldly;

namespace Frappe.ArchitectureTests;

/// <summary>Type-level rules: what the compiled code of each layer may actually use.</summary>
public sealed class DependencyTests
{
    private static readonly string[] Technology =
    [
        "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql", "NATS", "Quartz", "StackExchange.Redis",
        "Frappe.Platform",
    ];

    private static readonly string[] Messaging = ["Wolverine", "JasperFx"];

    // Core Wolverine (handlers, IMessageBus) is allowed in Application; its persistence integrations are not.
    private static readonly string[] MessagingPersistence =
        ["Wolverine.EntityFrameworkCore", "Wolverine.Persistence", "Wolverine.Postgresql", "Wolverine.RDBMS"];

    public static TheoryData<string> PureProjects =>
    [
        .. Solution.OfKind(ProjectKind.SharedKernel, ProjectKind.ContractsAbstractions, ProjectKind.ModuleDomain,
            ProjectKind.ModuleContracts).Select(p => p.Name),
    ];

    public static TheoryData<string> ApplicationProjects =>
        [.. Solution.OfKind(ProjectKind.ModuleApplication, ProjectKind.ApplicationAbstractions).Select(p => p.Name)];

    public static TheoryData<string> ModuleProjects => [.. Solution.Projects.Where(p => p.Module is not null).Select(p => p.Name)];

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(PureProjects))]
    public void PureProject_DependsOnNoFrameworkOrMessaging(string name) =>
        AssertNoDependency(name, [.. Technology, .. Messaging]);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ApplicationProjects))]
    public void Application_DependsOnNoInfrastructure(string name) =>
        AssertNoDependency(name, [.. Technology, .. MessagingPersistence]);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(ModuleProjects))]
    public void Module_UsesOtherModulesOnlyThroughTheirContracts(string name)
    {
        var module = Solution.Projects.Single(p => p.Name == name).Module;
        string[] otherInternals =
        [
            .. Solution.ModuleNames.Where(m => m != module)
                .SelectMany(m => new[] { $"Frappe.{m}.Domain", $"Frappe.{m}.Application", $"Frappe.{m}.Infrastructure" }),
        ];

        if (otherInternals.Length > 0)
            AssertNoDependency(name, otherInternals);
    }

    private static void AssertNoDependency(string projectName, string[] namespaces)
    {
        var project = Solution.Projects.Single(p => p.Name == projectName);
        var result = Types.InAssembly(Solution.LoadAssembly(project))
            .ShouldNot()
            .HaveDependencyOnAny(namespaces)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"{projectName} uses forbidden namespaces in: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
