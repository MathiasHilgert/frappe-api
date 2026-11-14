using NodaTime;

namespace Frappe.Geo.IntegrationTests.Sync;

/// <summary>A clock a test moves by hand.</summary>
internal sealed class SettableClock(Instant now) : IClock
{
    public Instant Now { get; set; } = now;

    public Instant GetCurrentInstant() => Now;
}
