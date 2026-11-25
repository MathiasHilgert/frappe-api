using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Frappe.Analyzers;

/// <summary>FRP002 (EVT-21, PERF-02): EF Core's retrying execution strategies are never enabled.</summary>
/// <remarks>
/// Reports a call to <c>EnableRetryOnFailure</c> declared by EF Core or a provider (a type under the
/// <c>Microsoft.EntityFrameworkCore</c> or <c>Npgsql</c> namespaces), and any type of ours that implements
/// <c>Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy</c>.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoExecutionStrategyAnalyzer : DiagnosticAnalyzer
{
    private const string RetryMethod = "EnableRetryOnFailure";
    private const string ExecutionStrategy = "Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy";

    private static readonly DiagnosticDescriptor Rule = new(
        "FRP002",
        "EVT-21: no EF Core execution-strategy retries",
        "EVT-21: {0} replays the whole unit of work; retries belong to the relay, the inbox and the client",
        "Reliability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An execution strategy replays effects that already left the process and forbids user transactions " +
                     "(EVT-21, PERF-02).");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var method = ((IInvocationOperation)context.Operation).TargetMethod;
        if (method.Name != RetryMethod || !IsEfCoreOrProvider(method.ContainingNamespace))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation(), RetryMethod));
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.AllInterfaces.Any(contract => contract.ToDisplayString() == ExecutionStrategy))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, type.Locations[0], type.Name));
        }
    }

    private static bool IsEfCoreOrProvider(INamespaceSymbol? scope)
    {
        var name = scope?.ToDisplayString() ?? string.Empty;
        return name == "Npgsql" || name.StartsWith("Npgsql.", System.StringComparison.Ordinal)
            || name == "Microsoft.EntityFrameworkCore" || name.StartsWith("Microsoft.EntityFrameworkCore.", System.StringComparison.Ordinal);
    }
}
