using Frappe.Testing;
using Xunit.Sdk;
using Xunit.v3;

// Readiness checks the database, so every host under test needs a real, migrated one.
[assembly: AssemblyFixture(typeof(FrappeDatabase))]

// Workaround for dotnet/aspnetcore#69684, as in Frappe.Platform.Persistence.IntegrationTests: start
// failures of Program are asserted here, and parallel host boots make WebApplicationFactory throw
// ObjectDisposedException instead. Remove once dotnet/aspnetcore#69685 ships.
[assembly: Parallelization(Mode = ParallelMode.None)]
