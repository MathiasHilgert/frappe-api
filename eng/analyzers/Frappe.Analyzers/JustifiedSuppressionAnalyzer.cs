using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Frappe.Analyzers;

/// <summary>FRP003 (CRAFT-10): every suppression in code states its reason.</summary>
/// <remarks>
/// A <c>[SuppressMessage]</c> or <c>[UnconditionalSuppressMessage]</c> needs a non-blank <c>Justification</c>. A
/// <c>#pragma warning disable</c> needs a <c>//</c> comment (not a <c>///</c> documentation line) at the end of its own line or on
/// the line right above it. Whether the scope is the narrowest one is left to review. Generated code (migrations) is not checked.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JustifiedSuppressionAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> SuppressionAttributes = ImmutableHashSet.Create(
        "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute",
        "System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute");

    private static readonly DiagnosticDescriptor Rule = new(
        "FRP003",
        "CRAFT-10: a suppression states its reason",
        "CRAFT-10: {0} has no reason; add a Justification or a comment that says why (starting with the rule id when it breaks a convention)",
        "Maintainability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An unexplained suppression cannot be reviewed or removed later (CRAFT-10).");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
        context.RegisterSyntaxTreeAction(AnalyzePragmas);
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        var attribute = (AttributeSyntax)context.Node;
        var type = context.SemanticModel.GetTypeInfo(attribute, context.CancellationToken).Type;
        if (type is null || !SuppressionAttributes.Contains(type.ToDisplayString()))
        {
            return;
        }

        var justification = attribute.ArgumentList?.Arguments
            .FirstOrDefault(argument => argument.NameEquals?.Name.Identifier.ValueText == "Justification");
        var reason = justification is null
            ? null
            : context.SemanticModel.GetConstantValue(justification.Expression, context.CancellationToken).Value as string;
        if (string.IsNullOrWhiteSpace(reason))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, attribute.GetLocation(), attribute.Name.ToString()));
        }
    }

    private static void AnalyzePragmas(SyntaxTreeAnalysisContext context)
    {
        var root = context.Tree.GetRoot(context.CancellationToken);
        var text = context.Tree.GetText(context.CancellationToken);
        var pragmas = root.DescendantTrivia(descendIntoTrivia: true)
            .Select(trivia => trivia.GetStructure())
            .OfType<PragmaWarningDirectiveTriviaSyntax>()
            .Where(pragma => pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword));

        foreach (var pragma in pragmas)
        {
            if (HasTrailingComment(pragma) || HasCommentAbove(pragma, text))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(Rule, pragma.GetLocation(), "#pragma warning disable"));
        }
    }

    private static bool HasTrailingComment(PragmaWarningDirectiveTriviaSyntax pragma) =>
        pragma.DescendantTrivia().Any(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia));

    private static bool HasCommentAbove(PragmaWarningDirectiveTriviaSyntax pragma, Microsoft.CodeAnalysis.Text.SourceText text)
    {
        var line = text.Lines.GetLineFromPosition(pragma.SpanStart).LineNumber;
        var above = line > 0 ? text.Lines[line - 1].ToString().TrimStart() : string.Empty;
        return above.StartsWith("//", System.StringComparison.Ordinal) && !above.StartsWith("///", System.StringComparison.Ordinal);
    }
}
