using System.Reflection;
using Frappe.UseCases;
using NetArchTest.Rules;

namespace Frappe.Architecture.Tests;

/// <summary>
/// A use case is reached through its port (<see cref="ICommandHandler{TCommand, TResult}"/>, <see cref="IQueryHandler{TQuery, TResult}"/>), never
/// through its concrete handler class: only the ports are decorated with the use-case gate, so a direct call would skip who-may-run-it (S7).
/// </summary>
public sealed class HandlerDependencyTests
{
    /// <summary>
    /// Every production Frappe assembly the hosts carry: found by name in the output folder, so a new module is scanned without an edit.
    /// </summary>
    private static readonly Assembly[] ModuleAssemblies =
    [
        .. Directory.EnumerateFiles(AppContext.BaseDirectory, "Frappe.*.dll")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !name.EndsWith("Tests", StringComparison.Ordinal) && !name.Contains(".Testing", StringComparison.Ordinal))
            .Select(name => System.Reflection.Assembly.Load(name)),
    ];

    /// <summary>The composition code that registers the handler classes behind their ports: the only place allowed to name them.</summary>
    private const string Composition = "Frappe.Observability.HandlerRegistration";

    private static bool IsHandler(Type type) =>
        type is { IsClass: true, IsAbstract: false } && type.GetInterfaces().Any(contract =>
            contract.IsGenericType && contract.GetGenericTypeDefinition() is var port
            && (port == typeof(ICommandHandler<,>) || port == typeof(IQueryHandler<,>)));

    [Test]
    public async Task No_type_depends_on_a_concrete_handler_class()
    {
        // Arrange
        var handlers = ModuleAssemblies.SelectMany(assembly => assembly.GetTypes()).Where(IsHandler).Select(type => type.FullName!).ToArray();

        // Act
        var offenders = Types.InAssemblies(ModuleAssemblies).ShouldNot().HaveDependencyOnAny(handlers).GetResult().FailingTypeNames ?? [];
        var outside = offenders
            .Where(name => !name.StartsWith(Composition, StringComparison.Ordinal)
                && !handlers.Any(handler => name == handler || name.StartsWith(handler + "/", StringComparison.Ordinal)))
            .ToArray();

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(handlers).IsNotEmpty();
            await Assert.That(outside).IsEmpty().Because($"a handler class is never a dependency, only its port: {string.Join(", ", outside)}");
        }
    }
}
