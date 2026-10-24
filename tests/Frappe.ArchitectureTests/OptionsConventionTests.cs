using System.Reflection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Frappe.ArchitectureTests;

/// <summary>
/// One options pattern everywhere: defaults on the options class, a <c>SectionName</c> constant when bound from a
/// section, and a source-generated <c>[OptionsValidator]</c> next to it (registered with <c>ValidateOnStart</c>).
/// </summary>
public sealed class OptionsConventionTests
{
    public static TheoryData<string> Projects => [.. Solution.Projects.Select(p => p.Name)];

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Projects))]
    public void Options_HaveAGeneratedValidator(string name)
    {
        var types = Solution.LoadAssembly(Solution.Projects.Single(p => p.Name == name)).GetTypes();
        var validated = types
            .Where(t => t.GetCustomAttribute<OptionsValidatorAttribute>() is not null)
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidateOptions<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToHashSet();

        var unvalidated = OptionsTypes(types).Where(t => !validated.Contains(t)).Select(t => t.FullName);

        unvalidated.ShouldBeEmpty("Every options class gets an [OptionsValidator] partial validator, run on start.");
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Projects))]
    public void Options_NameTheirSectionSectionName(string name)
    {
        var offenders = Solution.LoadAssembly(Solution.Projects.Single(p => p.Name == name)).GetTypes()
            .Where(t => t.GetField("Section", BindingFlags.Public | BindingFlags.Static) is { IsLiteral: true })
            .Select(t => t.FullName);

        offenders.ShouldBeEmpty("A configuration section constant is called SectionName.");
    }

    private static IEnumerable<Type> OptionsTypes(IEnumerable<Type> types) =>
        types.Where(t => t is { IsClass: true, IsAbstract: false, IsNested: false }
                         && t.Name.EndsWith("Options", StringComparison.Ordinal)
                         && t.Namespace?.StartsWith("Frappe.", StringComparison.Ordinal) == true);
}
