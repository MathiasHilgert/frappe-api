namespace Frappe.IntegrationTests;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class TestClock : TimeProvider
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public DateTimeOffset Now { get; private set; } = Start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;

    /// <summary>Back to the start, so a clock shared by a class never leaks one test's time into the next.</summary>
    public TestClock Reset()
    {
        Now = Start;
        return this;
    }
}
