using System.Reflection;
using Shouldly;

namespace Frappe.ArchitectureTests;

/// <summary>A module's domain is told the time; it never asks a clock, and its state changes only through behaviour.</summary>
public sealed class DomainPurityTests
{
    public static TheoryData<string> DomainProjects => [.. Solution.OfKind(ProjectKind.ModuleDomain).Select(p => p.Name)];

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(DomainProjects))]
    public void Domain_TakesNowAsAValue_NeverAClock(string name)
    {
        var offenders = Types(name)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(TimeProvider)))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}");

        offenders.ShouldBeEmpty();
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(DomainProjects))]
    public void Domain_HasNoNonPrivateSetters(string name)
    {
        var offenders = Types(name)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.DeclaredOnly))
            .Where(p => p.SetMethod is { IsPrivate: false } setter
                && !setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit"))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}");

        offenders.ShouldBeEmpty();
    }

    private static IEnumerable<Type> Types(string name) =>
        Solution.LoadAssembly(Solution.Projects.Single(p => p.Name == name)).GetTypes()
            .Where(t => !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)));
}
