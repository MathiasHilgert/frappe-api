namespace Frappe.Platform.IntegrationTests.Http;

/// <summary>What the client echoes.</summary>
/// <param name="Name">Any name.</param>
/// <param name="Count">Any count.</param>
internal sealed record EchoRequest(string? Name, int? Count);
