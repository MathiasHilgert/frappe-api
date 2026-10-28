using Frappe.TestUtilities;

// Readiness checks the database, so every host under test needs a real, migrated one.
[assembly: AssemblyFixture(typeof(FrappeDatabase))]
