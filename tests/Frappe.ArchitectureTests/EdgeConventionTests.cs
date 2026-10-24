using Shouldly;

namespace Frappe.ArchitectureTests;

/// <summary>
/// One HTTP edge: every endpoint lives in <c>&lt;Module&gt;.Infrastructure.Endpoints</c> as a <c>*Endpoints</c> route
/// group and answers a failure with <c>result.ToProblem()</c>; every error is declared in a <c>*Errors</c> catalogue.
/// </summary>
public sealed class EdgeConventionTests
{
    public static TheoryData<string> Modules => [.. Solution.ModuleNames];

    public static TheoryData<string> Projects => [.. Solution.Projects.Select(p => p.Name)];

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Modules))]
    public void Endpoint_NeverBuildsAProblemByHand(string module) =>
        EdgeRules.EndpointsBuildingProblems(Infrastructure(module)).ShouldBeEmpty(
            "Endpoints answer every failure with result.ToProblem(); give the error the right Kind and Metadata instead.");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Modules))]
    public void Endpoint_ReadsTheCallerThroughTheHelpers(string module) =>
        EdgeRules.EndpointsReadingTheCallerRaw(Infrastructure(module)).ShouldBeEmpty(
            "Endpoints read the caller with RequiredSession() and the address with ClientIp(), never HttpContext.User or RemoteIpAddress.");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Modules))]
    public void Endpoint_AnswersAnInfrastructureResponse_NeverAnApplicationDto(string module) =>
        EdgeRules.EndpointPayloadsThatAreNotResponses(Infrastructure(module)).ShouldBeEmpty(
            "Success bodies are <Module>.Infrastructure *Response records mapped from the Application result.");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Modules))]
    public void Endpoint_LivesInTheEndpointsFolder_AsARouteGroup(string module) =>
        EdgeRules.MisplacedEndpoints(Infrastructure(module)).ShouldBeEmpty(
            "Endpoints live in <Module>.Infrastructure.Endpoints, named *Endpoints (or *EndpointFilter).");

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Projects))]
    public void Error_IsDeclaredInACatalogue(string project) =>
        EdgeRules.ErrorsOutsideCatalogues(Solution.LoadAssembly(Solution.Projects.Single(p => p.Name == project))).ShouldBeEmpty(
            "Errors are declared in their module's *Errors catalogue, where the Kind and the message key are reviewed together.");

    private static CqrsRules.Layer Infrastructure(string module)
    {
        var name = $"Frappe.{module}.Infrastructure";
        return new CqrsRules.Layer(Solution.LoadAssembly(Solution.Projects.Single(p => p.Name == name)), name);
    }
}
