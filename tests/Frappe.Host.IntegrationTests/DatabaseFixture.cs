using Frappe.TestUtilities;

// Readiness checks the database and NATS, so every host under test needs a real, migrated database and a server.
[assembly: AssemblyFixture(typeof(FrappeDatabase))]
[assembly: AssemblyFixture(typeof(FrappeNats))]
