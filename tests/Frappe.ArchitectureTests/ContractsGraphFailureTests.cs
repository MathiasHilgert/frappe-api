using Shouldly;

namespace Frappe.ArchitectureTests;

/// <summary>The Contracts cycle check must name the modules that depend on each other.</summary>
public sealed class ContractsGraphFailureTests
{
    [Fact]
    public void FindCycle_NamesTwoModulesUsingEachOthersContracts()
    {
        Project[] projects =
        [
            new("Frappe.Tables.Contracts", ProjectKind.ModuleContracts, "Tables", []),
            new("Frappe.Tables.Application", ProjectKind.ModuleApplication, "Tables", ["Frappe.Orders.Contracts"]),
            new("Frappe.Orders.Contracts", ProjectKind.ModuleContracts, "Orders", []),
            new("Frappe.Orders.Infrastructure", ProjectKind.ModuleInfrastructure, "Orders", ["Frappe.Tables.Contracts"]),
        ];

        ContractsGraph.FindCycle(projects).ShouldBeOneOf("Tables -> Orders -> Tables", "Orders -> Tables -> Orders");
    }

    [Fact]
    public void FindCycle_IsNullForAOneWayDependency()
    {
        Project[] projects =
        [
            new("Frappe.Tables.Contracts", ProjectKind.ModuleContracts, "Tables", []),
            new("Frappe.Orders.Application", ProjectKind.ModuleApplication, "Orders", ["Frappe.Tables.Contracts"]),
        ];

        ContractsGraph.FindCycle(projects).ShouldBeNull();
    }
}
