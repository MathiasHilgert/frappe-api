using Frappe.Kernel.Geography;
using NodaTime;

namespace Frappe.Platform.IntegrationTests.Http;

/// <summary>The echo in every wire form.</summary>
/// <param name="Name">The name sent.</param>
/// <param name="CountryCode">A Kernel value object.</param>
/// <param name="Date">A local date.</param>
/// <param name="Color">An enum.</param>
/// <param name="Note">Always null.</param>
internal sealed record EchoResponse(
    string? Name,
    CountryCode CountryCode,
    LocalDate Date,
    EchoColor Color,
    string? Note
);
