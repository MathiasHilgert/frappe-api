using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Frappe.Analyzers;

/// <summary>
/// FRP001 (SEC-07, EVT-20, DOM-06): a record whose generated <c>ToString</c> would print a secret or an email declares its own.
/// </summary>
/// <remarks>
/// Only records are checked, since only their generated <c>ToString</c> prints members; a class or struct prints its type name.
/// A record is reported when it declares a public instance <see cref="string"/> property or field whose name ends with one of
/// <see cref="SensitiveWords"/> (<c>Password</c>, <c>NewPassword</c>, <c>UndoToken</c>, <c>OldEmail</c>), unless it overrides
/// <c>ToString</c> itself or a base record seals its own. Members of other types are left alone: an array or a collection prints
/// its type name, and a value object such as <c>Password</c> masks its own <c>ToString</c>. Codes and names are not matched
/// by name: error, country and language codes and plain names share those words, so review still covers them. Only members a
/// record declares itself are checked: a derived record re-prints its base's members, which review covers.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MaskedToStringAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The last word of a member name that marks it as a secret or as an email.</summary>
    internal static readonly ImmutableHashSet<string> SensitiveWords =
        ImmutableHashSet.Create("Password", "Passphrase", "Secret", "Token", "Plaintext", "Email");

    private static readonly DiagnosticDescriptor Rule = new(
        "FRP001",
        "SEC-07: a record holding a secret or an email masks its ToString",
        "SEC-07: record '{0}' prints {1} through its generated ToString; override ToString to mask it",
        "Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Records print every public member. One holding a secret, a token, a password or an email overrides ToString " +
                     "to print only what is safe (SEC-07, EVT-20, DOM-06).");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!type.IsRecord || DeclaresToString(type) || InheritsSealedToString(type))
        {
            return;
        }

        var sensitive = PrintedMembers(type).Where(IsSensitive).Select(member => member.Name).ToArray();
        if (sensitive.Length == 0)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, type.Locations[0], type.Name, string.Join(", ", sensitive)));
    }

    /// <summary>The members a record's generated <c>PrintMembers</c> prints: public instance properties and fields.</summary>
    private static IEnumerable<ISymbol> PrintedMembers(INamedTypeSymbol type) =>
        type.GetMembers().Where(member =>
            member is IPropertySymbol { IsIndexer: false } or IFieldSymbol
            && !member.IsStatic
            && member.DeclaredAccessibility == Accessibility.Public);

    private static bool IsSensitive(ISymbol member)
    {
        var type = member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null,
        };
        return type?.SpecialType == SpecialType.System_String && SensitiveWords.Contains(LastWord(member.Name));
    }

    /// <summary>The last PascalCase word of <paramref name="name"/>: <c>Token</c> in <c>UndoToken</c>.</summary>
    private static string LastWord(string name)
    {
        var start = name.Length - 1;
        while (start > 0 && !char.IsUpper(name[start]))
        {
            start--;
        }

        return name.Substring(start);
    }

    private static bool DeclaresToString(INamedTypeSymbol type) =>
        type.GetMembers(nameof(ToString)).OfType<IMethodSymbol>().Any(method => method.Parameters.IsEmpty && !method.IsImplicitlyDeclared);

    private static bool InheritsSealedToString(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(nameof(ToString)).OfType<IMethodSymbol>().Any(method => method.Parameters.IsEmpty && method.IsSealed))
            {
                return true;
            }
        }

        return false;
    }
}
