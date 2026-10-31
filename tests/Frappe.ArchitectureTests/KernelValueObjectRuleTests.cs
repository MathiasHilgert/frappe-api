using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Frappe.Kernel.Domain;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Assembly = System.Reflection.Assembly;

namespace Frappe.ArchitectureTests;

// A value object or typed id is valid only after its Create or From ran, so nothing may construct one directly.
public sealed class KernelValueObjectRuleTests
{
    private static readonly Assembly _kernel = Assembly.Load("Frappe.Kernel");

    private static readonly Architecture _architecture = new ArchLoader()
        .LoadAssemblies(_kernel)
        .Build();

    [Theory]
    [InlineData("Frappe.Kernel.Geography")]
    [InlineData("Frappe.Kernel.Currencies")]
    [InlineData("Frappe.Kernel.Localization")]
    public void A_kernel_value_object_has_no_public_constructor(string valueObjectNamespace)
    {
        MethodMembers()
            .That()
            .AreConstructors()
            .And()
            .AreDeclaredIn(Types().That().ResideInNamespace(valueObjectNamespace))
            .Should()
            .NotBePublic()
            .Check(_architecture);
    }

    [Fact]
    public void A_kernel_typed_id_has_no_public_constructor()
    {
        MethodMembers()
            .That()
            .AreConstructors()
            .And()
            .AreDeclaredIn(Types().That().ImplementInterface(typeof(IEntityId<>)))
            .Should()
            .NotBePublic()
            .Check(_architecture);
    }
}
