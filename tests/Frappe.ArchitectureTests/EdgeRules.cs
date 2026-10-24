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
        // Bare status results skip the problem shape (no code, no translated title): a failure is an Error instead.
        "Microsoft.AspNetCore.Http.TypedResults::Unauthorized",
        "Microsoft.AspNetCore.Http.TypedResults::NotFound",
        "Microsoft.AspNetCore.Http.TypedResults::BadRequest",
        "Microsoft.AspNetCore.Http.TypedResults::Conflict",
        "Microsoft.AspNetCore.Http.TypedResults::UnprocessableEntity",
        "Microsoft.AspNetCore.Http.TypedResults::Forbid",
        "Microsoft.AspNetCore.Http.TypedResults::Challenge",
        "Microsoft.AspNetCore.Http.TypedResults::StatusCode",
        "Microsoft.AspNetCore.Http.Results::Unauthorized",
        "Microsoft.AspNetCore.Http.Results::NotFound",
        "Microsoft.AspNetCore.Http.Results::BadRequest",
        "Microsoft.AspNetCore.Http.Results::Conflict",
        "Microsoft.AspNetCore.Http.Results::UnprocessableEntity",
        "Microsoft.AspNetCore.Http.Results::Forbid",
        "Microsoft.AspNetCore.Http.Results::Challenge",
        "Microsoft.AspNetCore.Http.Results::StatusCode",
    ];

    // Reading the caller straight off the HttpContext: endpoints use RequiredSession() and ClientIp() instead, so the
    // trusted-proxy and session rules live in one place.
    private static readonly string[] RawCallerReads =
    [
        "Microsoft.AspNetCore.Http.HttpContext::get_User",
        "Microsoft.AspNetCore.Http.ConnectionInfo::get_RemoteIpAddress",
    ];

    // Success payload wrappers whose type argument is the HTTP body.
    private static readonly string[] PayloadResults = ["Ok`1", "Created`1", "Accepted`1", "CreatedAtRoute`1", "AcceptedAtRoute`1", "JsonHttpResult`1"];

    /// <summary>Endpoint types (and their lambdas and state machines) whose code calls a problem factory.</summary>
    public static List<string> EndpointsBuildingProblems(CqrsRules.Layer infrastructure) =>
        EndpointTypesCalling(infrastructure, ProblemFactories);

    /// <summary>Endpoint types (and their lambdas and state machines) that read the user or the remote IP directly.</summary>
    public static List<string> EndpointsReadingTheCallerRaw(CqrsRules.Layer infrastructure) =>
        EndpointTypesCalling(infrastructure, RawCallerReads);

    /// <summary>
    /// Endpoint methods whose success body (<c>Ok&lt;T&gt;</c>, <c>Created&lt;T&gt;</c>, ...) is not an Infrastructure
    /// <c>*Response</c> type: an Application DTO must not become the HTTP contract by accident.
    /// </summary>
    public static List<string> EndpointPayloadsThatAreNotResponses(CqrsRules.Layer infrastructure) =>
        infrastructure.Types
            .Where(t => !t.IsNested && IsEndpoint(t))
            .SelectMany(t => t.GetMethods(AnyStatic).Select(m => (Type: t, Method: m)))
            .SelectMany(x => PayloadsOf(x.Method.ReturnType).Select(p => (x.Type, x.Method, Payload: p)))
            .Where(x => x.Payload.Assembly != infrastructure.Assembly || !x.Payload.Name.EndsWith("Response", StringComparison.Ordinal))
            .Select(x => $"{x.Type.FullName}.{x.Method.Name}: {x.Payload.Name}")
            .ToList();

    private static IEnumerable<Type> PayloadsOf(Type type)
    {
        if (!type.IsGenericType)
            yield break;
        if (PayloadResults.Contains(type.GetGenericTypeDefinition().Name, StringComparer.Ordinal))
            yield return ElementOf(type.GetGenericArguments()[0]);
        foreach (var payload in type.GetGenericArguments().SelectMany(PayloadsOf))
            yield return payload;
    }

    // A list body is judged by its items: Ok<IReadOnlyList<SessionSummaryResponse>> is fine.
    private static Type ElementOf(Type body)
    {
        if (body.IsArray)
            return body.GetElementType()!;
        return body.IsGenericType && body.GetGenericArguments() is [var item]
               && typeof(IEnumerable<>).MakeGenericType(item).IsAssignableFrom(body)
            ? item
            : body;
    }

    private static List<string> EndpointTypesCalling(CqrsRules.Layer infrastructure, string[] members)
    {
        using var module = ModuleDefinition.ReadModule(infrastructure.Assembly.Location);
        return module.GetTypes()
            .Where(t => Outermost(t) is var outer
                        && outer.Namespace.StartsWith(infrastructure.Namespace, StringComparison.Ordinal)
                        && IsEndpointName(outer.Name))
            .Where(t => t.Methods.Any(m => Calls(m, members)))
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

    private static bool Calls(MethodDefinition method, string[] members) =>
        method.HasBody && method.Body.Instructions
            .Select(i => i.Operand)
            .OfType<MethodReference>()
            .Any(m => members.Contains($"{m.DeclaringType.FullName}::{m.Name}", StringComparer.Ordinal));

    private static TypeDefinition Outermost(TypeDefinition type)
    {
        while (type.DeclaringType is { } declaring)
            type = declaring;
        return type;
    }
}
