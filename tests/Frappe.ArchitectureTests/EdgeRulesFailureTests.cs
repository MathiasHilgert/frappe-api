using Frappe.ArchitectureTests.Fixtures.Edge.Infrastructure;
using Frappe.ArchitectureTests.Fixtures.Edge.Infrastructure.Endpoints;
using Shouldly;

namespace Frappe.ArchitectureTests;

/// <summary>Each edge rule must catch the shape it forbids, named in its failure.</summary>
public sealed class EdgeRulesFailureTests
{
    private static readonly CqrsRules.Layer Infrastructure =
        new(typeof(EdgeRulesFailureTests).Assembly, "Frappe.ArchitectureTests.Fixtures.Edge.Infrastructure");

    [Fact]
    public void EndpointsBuildingProblems_NamesEveryEndpointShapingItsOwnProblem() =>
        EdgeRules.EndpointsBuildingProblems(Infrastructure).ShouldBe(
            [typeof(TypedProblemEndpoints).FullName!, typeof(ProblemDetailsEndpoints).FullName!, typeof(ResultsProblemEndpoints).FullName!],
            ignoreOrder: true);

    [Fact]
    public void MisplacedEndpoints_NamesEndpointsOutsideTheFolderOrSingular() =>
        EdgeRules.MisplacedEndpoints(Infrastructure).ShouldBe(
            [typeof(Fixtures.Edge.Infrastructure.Owners.StrayEndpoints).FullName!, typeof(Fixtures.Edge.Infrastructure.Endpoints.SingularEndpoint).FullName!],
            ignoreOrder: true);

    [Fact]
    public void ErrorsOutsideCatalogues_NamesAnErrorDeclaredInAHandler() =>
        EdgeRules.ErrorsOutsideCatalogues(typeof(EdgeRulesFailureTests).Assembly)
            .ShouldBe([$"{typeof(StrayErrorHandler).FullName}.{nameof(StrayErrorHandler.Missing)}"]);
}
