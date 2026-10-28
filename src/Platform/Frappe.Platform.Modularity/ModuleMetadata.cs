using System.Text.RegularExpressions;
using Frappe.Kernel.Authorization;
using Frappe.Kernel.Messaging;

namespace Frappe.Platform.Modularity;

/// <summary>
/// What a module declares about itself. Each value is validated here; the rules across modules (unique names,
/// schemas, route prefixes and EventId ranges, subscriptions some module publishes) run when the modules are added.
/// </summary>
public sealed partial record ModuleMetadata
{
    /// <summary>Initializes the metadata of a module.</summary>
    /// <param name="name">The module name, a lowercase identifier such as <c>geo</c>; also its OpenAPI tag.</param>
    /// <param name="schema">The PostgreSQL schema the module owns, a lowercase identifier.</param>
    /// <param name="routePrefix">The first path segment after <c>/v1</c>, lowercase kebab case such as <c>cash-shifts</c>.</param>
    /// <param name="eventIds">The log EventIds the module owns.</param>
    /// <param name="permissions">The permissions the module's messages require, each in the <see cref="Policy"/> format.</param>
    /// <param name="publishedEvents">The integration events the module publishes.</param>
    /// <param name="subscribedEvents">The integration events the module handles; each must be published by some module.</param>
    /// <exception cref="ArgumentException">A value is not in its format, or a type is not an integration event.</exception>
    public ModuleMetadata(
        string name,
        string schema,
        string routePrefix,
        EventIdRange eventIds,
        IReadOnlyCollection<string> permissions,
        IReadOnlyCollection<Type> publishedEvents,
        IReadOnlyCollection<Type> subscribedEvents
    )
    {
        Name = Matching(name, nameof(name), Identifier(), "a lowercase identifier such as 'geo'");
        Schema = Matching(
            schema,
            nameof(schema),
            Identifier(),
            "a lowercase identifier such as 'geo'"
        );
        RoutePrefix = Matching(
            routePrefix,
            nameof(routePrefix),
            KebabCase(),
            "lowercase kebab case such as 'cash-shifts'"
        );
        ArgumentNullException.ThrowIfNull(eventIds);
        ArgumentNullException.ThrowIfNull(permissions);
        foreach (string permission in permissions)
        {
            // The Policy constructor owns the permission format, so a typo fails as it would in Perms.
            _ = new Policy(permission);
        }

        EventIds = eventIds;
        Permissions = permissions;
        PublishedEvents = IntegrationEvents(publishedEvents, nameof(publishedEvents));
        SubscribedEvents = IntegrationEvents(subscribedEvents, nameof(subscribedEvents));
    }

    /// <summary>Gets the module name, also its OpenAPI tag.</summary>
    public string Name { get; }

    /// <summary>Gets the PostgreSQL schema the module owns.</summary>
    public string Schema { get; }

    /// <summary>Gets the first path segment after <c>/v1</c>.</summary>
    public string RoutePrefix { get; }

    /// <summary>Gets the log EventIds the module owns.</summary>
    public EventIdRange EventIds { get; }

    /// <summary>Gets the permissions the module's messages require.</summary>
    public IReadOnlyCollection<string> Permissions { get; }

    /// <summary>Gets the integration events the module publishes.</summary>
    public IReadOnlyCollection<Type> PublishedEvents { get; }

    /// <summary>Gets the integration events the module handles.</summary>
    public IReadOnlyCollection<Type> SubscribedEvents { get; }

    private static string Matching(string value, string parameter, Regex format, string expected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        return format.IsMatch(value)
            ? value
            : throw new ArgumentException($"'{value}' is not {expected}.", parameter);
    }

    private static IReadOnlyCollection<Type> IntegrationEvents(
        IReadOnlyCollection<Type> types,
        string parameter
    )
    {
        ArgumentNullException.ThrowIfNull(types, parameter);
        foreach (Type? type in types)
        {
            if (type is null || type.IsAbstract || !type.IsAssignableTo(typeof(IIntegrationEvent)))
            {
                throw new ArgumentException(
                    $"'{type}' is not a concrete {nameof(IIntegrationEvent)} type.",
                    parameter
                );
            }
        }

        return types;
    }

    [GeneratedRegex(
        "^[a-z][a-z0-9_]*\\z",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100
    )]
    private static partial Regex Identifier();

    [GeneratedRegex(
        "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*\\z",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100
    )]
    private static partial Regex KebabCase();
}
