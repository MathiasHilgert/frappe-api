using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Frappe.Analyzers.UnitTests;

/// <summary>Compiles C# source in memory against the running framework and returns what one analyzer reports on it.</summary>
internal static class AnalyzerRun
{
    private static readonly ImmutableArray<MetadataReference> FrameworkReferences =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)),
    ];

    /// <summary>
    /// The analyzer's diagnostics on <paramref name="sources"/>. The source must compile and the analyzer must not throw, so a
    /// case without diagnostics cannot pass by accident.
    /// </summary>
    public static async Task<ImmutableArray<Diagnostic>> DiagnosticsOf(DiagnosticAnalyzer analyzer, params string[] sources)
    {
        var compilation = CSharpCompilation.Create(
            "Sample",
            sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, path: $"Sample{index}.cs")),
            FrameworkReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidOperationException("The sample does not compile: " + string.Join("; ", errors.Select(e => e.ToString())));
        }

        var options = new CompilationWithAnalyzersOptions(
            new AnalyzerOptions([]),
            onAnalyzerException: null,
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: false);
        var diagnostics = await compilation.WithAnalyzers([analyzer], options).GetAllDiagnosticsAsync();

        var crashes = diagnostics.Where(diagnostic => diagnostic.Id == "AD0001").ToArray();
        if (crashes.Length > 0)
        {
            throw new InvalidOperationException("The analyzer threw: " + string.Join("; ", crashes.Select(c => c.ToString())));
        }

        return [.. diagnostics.Where(diagnostic => analyzer.SupportedDiagnostics.Any(d => d.Id == diagnostic.Id))];
    }
}
