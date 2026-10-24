using System.Reflection;
using Frappe.SharedKernel.Validation;
using Mono.Cecil;

namespace Frappe.ArchitectureTests;

/// <summary>The HTTP edge and error checks, written against any assembly so their own failure can be tested.</summary>
internal static class EdgeRules
{
    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly string[] EndpointSuffixes = ["Endpoint", "Endpoints", "EndpointFilter"];

    private static readonly string[] AllowedEndpointSuffixes = ["Endpoints", "EndpointFilter"];

    // Building a problem body by hand: the factories and the body types. ToProblem() is the only way an endpoint
    // answers a failure.
    private static readonly string[] ProblemFactories =
    [
        "Microsoft.AspNetCore.Http.TypedResults::Problem",
        "Microsoft.AspNetCore.Http.TypedResults::ValidationProblem",
        "Microsoft.AspNetCore.Http.Results::Problem",
        "Microsoft.AspNetCore.Http.Results::ValidationProblem",
        "Microsoft.AspNetCore.Mvc.ProblemDetails::.ctor",
        "Microsoft.AspNetCore.Http.HttpValidationProblemDetails::.ctor",
    ];

    /// <summary>Endpoint types (and their lambdas and state machines) whose code calls a problem factory.</summary>
    public static List<string> EndpointsBuildingProblems(CqrsRules.Layer infrastructure)
    {
        using var module = ModuleDefinition.ReadModule(infrastructure.Assembly.Location);
        return module.GetTypes()
            .Where(t => Outermost(t) is var outer
                        && outer.Namespace.StartsWith(infrastructure.Namespace, StringComparison.Ordinal)
                        && IsEndpointName(outer.Name))
            .Where(t => t.Methods.Any(CallsAProblemFactory))
            .Select(t => Outermost(t).FullName.Replace('/', '+'))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Endpoint types outside <c>&lt;Infrastructure&gt;.Endpoints</c>, or not named <c>*Endpoints</c> (a route group)
    /// or <c>*EndpointFilter</c>.
    /// </summary>
    public static List<string> MisplacedEndpoints(CqrsRules.Layer infrastructure) =>
        infrastructure.Types
            .Where(t => !t.IsNested && IsEndpoint(t))
            .Where(t => t.Namespace != $"{infrastructure.Namespace}.Endpoints"
                        || !AllowedEndpointSuffixes.Any(s => t.Name.EndsWith(s, StringComparison.Ordinal)))
            .Select(t => t.FullName!)
            .ToList();

    /// <summary>Static <see cref="Error"/> fields or properties declared outside a <c>*Errors</c> catalogue.</summary>
    public static List<string> ErrorsOutsideCatalogues(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => !t.Name.EndsWith("Errors", StringComparison.Ordinal))
            .SelectMany(t => t.GetFields(AnyStatic).Where(f => f.FieldType == typeof(Error) && !f.Name.Contains('<', StringComparison.Ordinal))
                .Select(f => $"{t.FullName}.{f.Name}")
                .Concat(t.GetProperties(AnyStatic).Where(p => p.PropertyType == typeof(Error)).Select(p => $"{t.FullName}.{p.Name}")))
            .ToList();

    private static bool IsEndpoint(Type type) => IsEndpointName(type.Name);

    private static bool IsEndpointName(string name) => EndpointSuffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal));

    private static bool CallsAProblemFactory(MethodDefinition method) =>
        method.HasBody && method.Body.Instructions
            .Select(i => i.Operand)
            .OfType<MethodReference>()
            .Any(m => ProblemFactories.Contains($"{m.DeclaringType.FullName}::{m.Name}", StringComparer.Ordinal));

    private static TypeDefinition Outermost(TypeDefinition type)
    {
        while (type.DeclaringType is { } declaring)
            type = declaring;
        return type;
    }
}
