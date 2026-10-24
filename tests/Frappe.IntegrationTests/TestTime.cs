using Microsoft.Extensions.Time.Testing;

namespace Frappe.IntegrationTests;

/// <summary>The instant every test clock starts at.</summary>
internal static class TestTime
{
    public static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public static FakeTimeProvider NewClock() => new(Start);

    /// <summary>Back to the start, so a clock shared by a class never leaks one test's time into the next.</summary>
    public static FakeTimeProvider Reset(this FakeTimeProvider clock)
    {
        clock.AdjustTime(Start);
        return clock;
    }
}
