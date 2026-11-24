namespace Frappe.Identity.Application.Sessions.ListMySessions;

/// <summary>What a user agent says about the client: its browser and its operating system, each none when not recognized.</summary>
/// <param name="Browser">The browser, such as <c>Firefox</c>.</param>
/// <param name="Os">The operating system, such as <c>Windows</c>.</param>
internal sealed record ClientAgent(string? Browser, string? Os);
