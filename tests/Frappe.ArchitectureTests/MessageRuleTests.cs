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
        string[] offenders =
        [
            .. _frappeAssemblies
                .SelectMany(static assembly => assembly.GetTypes())
                .Where(static type => !type.IsInterface)
                .SelectMany(static type =>
                    type.GetInterfaces()
                        .Where(contract =>
                            contract.IsGenericType
                            && (
                                contract.GetGenericTypeDefinition() == typeof(ICommand<,>)
                                || contract.GetGenericTypeDefinition() == typeof(IQuery<,>)
                            )
                            && contract.GenericTypeArguments[0] != type
                        )
                        .Select(contract => $"{type.FullName} implements {contract}")
                ),
        ];

        Assert.Empty(offenders);
    }
}
