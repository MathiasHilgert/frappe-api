using System.Reflection;
using Frappe.Access.Contracts;
using Frappe.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Access.Application;

/// <summary>
/// What a host proves about its use cases before it serves one (S7): every request declares exactly one marker, and every handler chain passes
/// through the use-case gate. Each finding names the request, and all of them are reported together.
/// </summary>
internal static class UseCaseCheck
{
    private const int MaxChainDepth = 16;

#pragma warning disable S3011 // The decorators keep what they wrap in private fields: that is the only place the chain can be read from.
    private const BindingFlags AllFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
#pragma warning restore S3011

    /// <summary>The findings about the handler ports of the final container: markers first, then chains.</summary>
    public static IReadOnlyList<string> Findings(IServiceProvider provider, IReadOnlyCollection<Type> ports)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(ports);
        return [.. MarkerFindings(ports.Select(RequestOf).Distinct()), .. ChainFindings(provider, ports)];
    }

    /// <summary>A finding for every request that declares no marker or both, and for one whose rule cannot be read.</summary>
    public static IReadOnlyList<string> MarkerFindings(IEnumerable<Type> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        List<string> findings = [];
        foreach (var request in requests.OrderBy(request => request.Name, StringComparer.Ordinal))
        {
            var guarded = typeof(IGuarded).IsAssignableFrom(request);
            var isPublic = typeof(IPublic).IsAssignableFrom(request);
            if (guarded == isPublic)
            {
                var declares = guarded ? "both IGuarded and IPublic" : "neither IGuarded nor IPublic";
                findings.Add($"{request.Name} declares {declares}: it needs exactly one.");
            }
            else if (guarded && CannotReadRule(request))
            {
                findings.Add($"{request.Name}.Allowed cannot be read: it must return a rule.");
            }
        }

        return findings;
    }

    /// <summary>
    /// Whether the request's <c>Allowed</c> fails to produce a rule: building its table is the only way the gate reads it, so it proves the same.
    /// </summary>
    private static bool CannotReadRule(Type request)
    {
        try
        {
            _ = UseCaseTable.From([request]);
            return false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or TargetInvocationException)
        {
            return true;
        }
    }

    private static List<string> ChainFindings(IServiceProvider provider, IReadOnlyCollection<Type> ports)
    {
        List<string> findings = [];
        using var scope = provider.CreateScope();
        try
        {
            _ = scope.ServiceProvider.GetRequiredService<IUseCaseGate>();
        }
        catch (TargetInvocationException)
        {
            return []; // The gate reads every rule when it is built: the unreadable one is already named by the marker findings.
        }
        catch (InvalidOperationException)
        {
            return ["The use-case gate cannot be built: the host needs an ICaller (a session caller in the Api, the system in the Worker)."];
        }

        foreach (var port in ports.OrderBy(port => RequestOf(port).Name, StringComparer.Ordinal))
        {
            var name = RequestOf(port).Name;
            try
            {
                if (scope.ServiceProvider.GetService(port) is not { } handler || !PassesGate(handler, port))
                {
                    findings.Add($"The handler of {name} does not pass through the use-case gate: register it through AddHandlers.");
                }
            }
            catch (InvalidOperationException)
            {
                // A handler that cannot be built cannot run either, so it cannot bypass the gate; the gate itself is proven above.
            }
        }

        return findings;
    }

    /// <summary>Whether a decorator of the chain holds the gate: each decorator keeps the handler it wraps in a field of the port's type.</summary>
    private static bool PassesGate(object handler, Type port)
    {
        object? current = handler;
        for (var depth = 0; current is not null && depth < MaxChainDepth; depth++)
        {
            var fields = FieldsOf(current.GetType()).ToArray();
            if (fields.Any(field => field.FieldType == typeof(IUseCaseGate)))
            {
                return true;
            }

            var instance = current;
            current = fields.Where(field => port.IsAssignableFrom(field.FieldType)).Select(field => field.GetValue(instance)).FirstOrDefault();
        }

        return false;
    }

    private static IEnumerable<FieldInfo> FieldsOf(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(AllFields))
            {
                yield return field;
            }
        }
    }

    private static Type RequestOf(Type port) => port.GetGenericArguments()[0];
}
