namespace Frappe.Geo.IntegrationTests;

// One reader command as sent: its SQL and parameters.
internal sealed record RecordedCommand(
    string Sql,
    IReadOnlyList<(string Name, object? Value)> Parameters
);
