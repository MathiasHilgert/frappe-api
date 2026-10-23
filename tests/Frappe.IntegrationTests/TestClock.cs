namespace Frappe.IntegrationTests;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
