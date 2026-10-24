using System.Reflection;
using System.Runtime.CompilerServices;
using Frappe.Application.Abstractions.Messaging;
using Frappe.SharedKernel.Validation;
using NetArchTest.Rules;

namespace Frappe.ArchitectureTests;

/// <summary>The CQRS lite checks, written against any assembly so their own failure can be tested.</summary>
internal static class CqrsRules
{
    private const BindingFlags AnyMethod =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static readonly string[] HandlerMethodNames = ["Handle", "HandleAsync", "Consume", "ConsumeAsync"];

    private static readonly string[] EndpointSuffixes = ["Endpoint", "Endpoints", "EndpointFilter"];

    /// <summary>The types of one assembly under <paramref name="Namespace"/>.</summary>
    public sealed record Layer(Assembly Assembly, string Namespace)
    {
        public IEnumerable<Type> Types =>
            Assembly.GetTypes().Where(t => t.Namespace?.StartsWith(Namespace, StringComparison.Ordinal) == true);
    }

    /// <summary>Messages declared in the layer whose handler sits there too, that are not marked records.</summary>
    public static List<string> UnmarkedMessages(Layer application) =>
        Handlers(application)
            .Select(h => h.Message)
            .Where(m => m.Assembly == application.Assembly)
            .Where(m => !IsRecord(m) || (!IsCommand(m) && QueryResult(m) is null))
            .Select(m => m.FullName!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Endpoint types (and their lambdas and state machines) that use an Application service.</summary>
    public static List<string> EndpointsCallingServices(Layer application, Layer infrastructure)
    {
        string[] services = [.. Services(application).Select(t => t.FullName!)];
        if (services.Length == 0)
            return [];

        return DependingTypes(infrastructure, services)
            .Where(t => EndpointSuffixes.Any(s => Outermost(t).Name.EndsWith(s, StringComparison.Ordinal)))
            .Select(t => Outermost(t).FullName!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Query handlers that depend on a repository, the only way to load an aggregate for writing.</summary>
    public static List<string> WritingQueryHandlers(Layer application)
    {
        var repositories = application.Assembly.GetTypes()
            .Where(t => t.IsInterface && t.Name.EndsWith("Repository", StringComparison.Ordinal))
            .Select(t => t.FullName!)
            .ToArray();
        var queryHandlers = Handlers(application)
            .Where(h => QueryResult(h.Message) is not null)
            .Select(h => h.Handler)
            .ToHashSet();
        if (repositories.Length == 0 || queryHandlers.Count == 0)
            return [];

        return DependingTypes(application, repositories)
            .Where(t => queryHandlers.Contains(Outermost(t)))
            .Select(t => Outermost(t).FullName!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Command handlers whose answer carries a type some query answers with.</summary>
    public static List<string> CommandHandlersReturningReadModels(Layer application)
    {
        var handlers = Handlers(application).ToList();
        var readModels = handlers
            .Select(h => QueryResult(h.Message))
            .OfType<Type>()
            .SelectMany(Unwrap)
            .Where(IsOwnType)
            .ToHashSet();

        return handlers
            .Where(h => IsCommand(h.Message))
            .Where(h => Unwrap(h.Method.ReturnType).Any(readModels.Contains))
            .Select(h => $"{h.Handler.FullName}.{h.Method.Name}({h.Message.Name})")
            .ToList();
    }

    /// <summary>
    /// Command handlers that answer with anything but <c>Result</c> or <c>Result&lt;T&gt;</c>: awaited through
    /// <c>Task</c>/<c>ValueTask</c>, and first in a tuple when the rest cascades. A background command is the
    /// reverse: nobody reads its answer, so a <c>Result</c> there would cascade as an unrouted message.
    /// </summary>
    public static List<string> CommandHandlersNotReturningResult(Layer application) =>
        Handlers(application)
            .Where(h => IsCommand(h.Message))
            .Where(h => IsResult(Answer(h.Method.ReturnType)) == IsBackgroundCommand(h.Message))
            .Select(h => $"{h.Handler.FullName}.{h.Method.Name}({h.Message.Name})")
            .ToList();

    /// <summary>
    /// Classes holding use-case logic that a caller would have to invoke directly: concrete, instantiable, not records
    /// and not handlers. Ports (interfaces) and messages are not services.
    /// </summary>
    public static IEnumerable<Type> Services(Layer application) =>
        application.Types.Where(t =>
            t is { IsClass: true, IsAbstract: false, IsNested: false }
            && !IsRecord(t)
            && !t.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
            && !t.Name.EndsWith("Handler", StringComparison.Ordinal)
            && !t.Name.EndsWith("Marker", StringComparison.Ordinal));

    private static IEnumerable<(Type Handler, MethodInfo Method, Type Message)> Handlers(Layer application) =>
        from type in application.Types
        where type.Name.EndsWith("Handler", StringComparison.Ordinal) && !type.IsNested
        from method in type.GetMethods(AnyMethod)
        where method.IsPublic && HandlerMethodNames.Contains(method.Name, StringComparer.Ordinal)
        let parameters = method.GetParameters()
        where parameters.Length > 0
        select (type, method, parameters[0].ParameterType);

    private static IEnumerable<Type> DependingTypes(Layer layer, string[] dependencies) =>
        Types.InAssembly(layer.Assembly)
            .That().ResideInNamespaceStartingWith(layer.Namespace)
            .And().HaveDependencyOnAny(dependencies)
            .GetTypes();

    private static Type Answer(Type returnType)
    {
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() is var awaited
            && (awaited == typeof(Task<>) || awaited == typeof(ValueTask<>)))
            returnType = returnType.GetGenericArguments()[0];
        return returnType.IsGenericType && returnType.FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) == true
            ? returnType.GetGenericArguments()[0]
            : returnType;
    }

    private static bool IsResult(Type type) => type == typeof(Result) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>));

    private static bool IsCommand(Type message) => typeof(ICommand).IsAssignableFrom(message);

    private static bool IsBackgroundCommand(Type message) => typeof(IBackgroundCommand).IsAssignableFrom(message);

    private static Type? QueryResult(Type message) =>
        message.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>))
            ?.GetGenericArguments()[0];

    // Records get a compiler-generated EqualityContract; plain classes do not.
    private static bool IsRecord(Type type) =>
        type.GetProperty("EqualityContract", BindingFlags.NonPublic | BindingFlags.Instance) is not null;

    private static bool IsOwnType(Type type) => type.Assembly.GetName().Name?.StartsWith("Frappe.", StringComparison.Ordinal) == true;

    /// <summary>The type and every generic argument inside it: Task&lt;Result&lt;OwnerProfile&gt;&gt; yields all three.</summary>
    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;
        Type[] inner = [];
        if (type.IsArray)
            inner = [type.GetElementType()!];
        else if (type.IsGenericType)
            inner = type.GetGenericArguments();
        foreach (var nested in inner.SelectMany(Unwrap))
            yield return nested;
    }

    private static Type Outermost(Type type)
    {
        while (type.DeclaringType is { } declaring)
            type = declaring;
        return type;
    }
}
