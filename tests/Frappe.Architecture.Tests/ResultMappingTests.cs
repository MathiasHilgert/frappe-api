using System.Reflection;
using Frappe.Http;
using Frappe.Testing;

namespace Frappe.Architecture.Tests;

/// <summary>One way to map a <c>Result</c> to HTTP: <c>ToTypedHttpResult</c> (S8a, HTTP-06).</summary>
public sealed class ResultMappingTests
{
    private static readonly Assembly Http = typeof(ResultHttpExtensions).Assembly;

    [Test]
    public async Task The_platform_offers_ToTypedHttpResult_as_the_only_way_to_answer_a_result()
    {
        // Act
        var mappers = Http.GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.Name.Contains("HttpResult", StringComparison.Ordinal) && method.Name.StartsWith("To", StringComparison.Ordinal))
            .Select(method => method.Name)
            .Distinct()
            .ToArray();

        // Assert
        await Assert.That(mappers).IsEquivalentTo(["ToTypedHttpResult"]);
    }

    [Test]
    public async Task A_module_Http_project_cannot_hand_write_the_problem_the_mapping_produces()
    {
        // Arrange
        var of = typeof(ErrorsProblemHttpResult).GetMethod(nameof(ErrorsProblemHttpResult.Of))!;
        var symbol = $"M:{of.DeclaringType!.FullName}.{of.Name}({string.Join(",", of.GetParameters().Select(p => p.ParameterType.FullName))})";
        var root = RepositoryRoot.FullPath;

        // Act
        var banned = await File.ReadAllLinesAsync(Path.Combine(root, "eng", "BannedSymbols.Http.txt"));
        var props = await File.ReadAllTextAsync(Path.Combine(root, "Directory.Build.props"));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(banned.Any(line => line.StartsWith(symbol + ";", StringComparison.Ordinal))).IsTrue();
            await Assert.That(props).Contains("EndsWith('.Http')");
            await Assert.That(props).Contains("BannedSymbols.Http.txt");
        }
    }
}
