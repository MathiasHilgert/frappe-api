using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;
using Frappe.Access.Contracts;

namespace Frappe.Access.Application;

/// <summary>
/// What every request declared, built once from the static <see cref="IGuarded.Allowed"/> of the guarded ones and the markers of the rest, then
/// only read: a lookup per request, no reflection. A request it does not know has declared nothing.
/// </summary>
internal sealed class UseCaseTable
{
    private static readonly MethodInfo AllowedOf = typeof(UseCaseTable)
        .GetMethod(nameof(ReadAllowed), BindingFlags.Public | BindingFlags.Static)!;

    private readonly FrozenDictionary<Type, UseCaseDeclaration> _declarations;
    private readonly ConcurrentDictionary<Type, UseCaseDeclaration> _unknown = new();

    private UseCaseTable(FrozenDictionary<Type, UseCaseDeclaration> declarations) => _declarations = declarations;

    /// <summary>Reads the declaration of every distinct request type of <paramref name="requests"/>.</summary>
    public static UseCaseTable From(IEnumerable<Type> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        return new UseCaseTable(requests.Distinct().ToFrozenDictionary(request => request, Declare));
    }

    /// <summary>The declaration of <paramref name="request"/>; one without a marker when the type is unknown or declared neither.</summary>
    public UseCaseDeclaration For(Type request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _declarations.GetValueOrDefault(request) ?? _unknown.GetOrAdd(request, type => new UseCaseDeclaration(type.Name, null, false, []));
    }

    private static UseCaseDeclaration Declare(Type request)
    {
        var rule = typeof(IGuarded).IsAssignableFrom(request)
            ? (CallerRule?)AllowedOf.MakeGenericMethod(request).Invoke(null, null)
                ?? throw new InvalidOperationException($"{request.Name}.Allowed is null.")
            : null;
        var actsOn = request.GetInterfaces()
            .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IActsOn<>))
            .Select(contract => contract.GetGenericArguments()[0])
            .ToArray();
        return new UseCaseDeclaration(request.Name, rule, rule is null && typeof(IPublic).IsAssignableFrom(request), actsOn);
    }

    /// <summary>The static <see cref="IGuarded.Allowed"/> of <typeparamref name="TRequest"/>, read through its constraint.</summary>
    public static CallerRule ReadAllowed<TRequest>()
        where TRequest : IGuarded => TRequest.Allowed;
}
