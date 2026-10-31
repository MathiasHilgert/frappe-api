using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using ErrorOr;
using Frappe.Kernel.Domain;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Assembly = System.Reflection.Assembly;

namespace Frappe.ArchitectureTests;

// A request binds raw input: a value object member would fail binding with a 400 before its Create could report
// the 422 with a code and pointer, so requests stay primitive and the handler builds the value objects.
public sealed class RequestRuleTests
{
    private static readonly IArchRule _requestsStayPrimitive = Types()
        .That()
        .HaveNameEndingWith("Request")
        .Should()
        .NotDependOnAny(Types().That().ImplementInterface(typeof(IEntityId<>)))
        // Vacuous until the first module ships a request; the fixture test below proves the rule bites.
        .WithoutRequiringPositiveResults();

    // Every Frappé assembly the Host ships, found by file so a new module is covered at once.
    private static readonly Assembly[] _shipped =
    [
        .. Directory
            .GetFiles(AppContext.BaseDirectory, "Frappe.*.dll")
            .Select(static file => Path.GetFileNameWithoutExtension(file))
            .Where(static name =>
                !name.EndsWith("Tests", StringComparison.Ordinal)
                && !string.Equals(name, "Frappe.TestUtilities", StringComparison.Ordinal)
            )
            .Order(StringComparer.Ordinal)
            .Select(static name => Assembly.Load(name)),
    ];

    [Fact]
    public void A_request_record_declares_no_kernel_value_object_member()
    {
        _requestsStayPrimitive.Check(new ArchLoader().LoadAssemblies(_shipped).Build());
    }

    [Fact]
    public void The_rule_catches_a_request_with_a_typed_id_member()
    {
        Architecture fixture = new ArchLoader()
            .LoadAssemblies(typeof(RequestRuleTests).Assembly, typeof(IEntityId<>).Assembly)
            .Build();

        Assert.False(_requestsStayPrimitive.HasNoViolations(fixture));
    }

    // Fixtures for the rule above: a typed id and a request that binds it.
    internal readonly record struct OwnerId : IEntityId<OwnerId>
    {
        private OwnerId(Guid value) => Value = value;

        public Guid Value { get; }

        public static OwnerId NewId() => new(Guid.CreateVersion7());

        public static ErrorOr<OwnerId> From(Guid value) =>
            EntityId.Validate(value).Then(static v => new OwnerId(v));
    }

    internal sealed record OwnerChangeRequest(OwnerId? OwnerId);
}
