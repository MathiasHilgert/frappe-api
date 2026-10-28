using System.Reflection;
using Frappe.Kernel.Authorization;
using Frappe.Kernel.Domain;
using Frappe.Kernel.Messaging;
using Assembly = System.Reflection.Assembly;

namespace Frappe.ArchitectureTests;

public sealed class MessageRuleTests
{
    // Every Frappe assembly next to the tests, found by file so a new module is covered at once.
    private static readonly Assembly[] _frappeAssemblies =
    [
        .. Directory
            .GetFiles(AppContext.BaseDirectory, "Frappe.*.dll")
            .Order(StringComparer.Ordinal)
            .Select(static file => Assembly.Load(Path.GetFileNameWithoutExtension(file))),
    ];

    // The dispatcher authorizes with TSelf.Policy, so a message whose TSelf names another type
    // would be checked against that other type's policy.
    [Fact]
    public void Every_message_names_itself_as_its_self_type()
    {
        (Type Message, Type Contract)[] messages =
        [
            .. _frappeAssemblies
                .SelectMany(static assembly => assembly.GetTypes())
                .Where(static type => !type.IsInterface)
                .SelectMany(static type =>
                    type.GetInterfaces()
                        .Where(static contract =>
                            contract.IsGenericType
                            && (
                                contract.GetGenericTypeDefinition() == typeof(ICommand<,>)
                                || contract.GetGenericTypeDefinition() == typeof(IQuery<,>)
                            )
                        )
                        .Select(contract => (type, contract))
                ),
        ];

        // The samples below prove the scan finds messages, so the rule can never pass vacuously.
        Assert.Contains(messages, static found => found.Message == typeof(SampleCommand));
        Assert.Contains(messages, static found => found.Message == typeof(SampleQuery));
        Assert.Empty(
            messages
                .Where(static found => found.Contract.GenericTypeArguments[0] != found.Message)
                .Select(static found => $"{found.Message.FullName} implements {found.Contract}")
        );
    }

    // Durable queues, schedules and dead letters store a message under its identity, so a missing or
    // repeated one would strand persisted messages.
    [Fact]
    public void Every_persisted_message_has_a_unique_stable_identity()
    {
        Type[] messages =
        [
            .. _frappeAssemblies
                .SelectMany(static assembly => assembly.GetTypes())
                .Where(static type =>
                    type is { IsAbstract: false, IsInterface: false } && IsPersisted(type)
                ),
        ];
        string[] identities =
        [
            .. messages
                .Select(static type => type.GetCustomAttribute<MessageIdentityAttribute>()?.Name)
                .OfType<string>(),
        ];

        Assert.Contains(typeof(SampleCommand), messages);
        Assert.Empty(
            messages
                .Where(static type => !Attribute.IsDefined(type, typeof(MessageIdentityAttribute)))
                .Select(static type =>
                    $"{type.FullName} is stored by the bus but has no [MessageIdentity]; add one with a name that never changes."
                )
        );
        Assert.Equal(identities.Length, identities.Distinct(StringComparer.Ordinal).Count());
    }

    private static bool IsPersisted(Type type) =>
        type.IsAssignableTo(typeof(IDomainEvent))
        || type.IsAssignableTo(typeof(IIntegrationEvent))
        || type.GetInterfaces()
            .Any(static contract =>
                contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ICommand<,>)
            );

    [MessageIdentity("sample.run")]
    private sealed record SampleCommand : ICommand<SampleCommand, int>
    {
        public static Policy Policy { get; } = new("sample.run");
    }

    private sealed record SampleQuery : IQuery<SampleQuery, string>
    {
        public static Policy Policy { get; } = new("sample.read");
    }
}
