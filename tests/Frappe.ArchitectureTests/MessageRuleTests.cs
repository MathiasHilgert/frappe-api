using Frappe.Kernel.Authorization;
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

    private sealed record SampleCommand : ICommand<SampleCommand, int>
    {
        public static Policy Policy { get; } = new("sample.run");
    }

    private sealed record SampleQuery : IQuery<SampleQuery, string>
    {
        public static Policy Policy { get; } = new("sample.read");
    }
}
